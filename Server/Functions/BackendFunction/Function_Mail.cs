using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 우편 — 뒤끝 관리자·랭킹 우편을 읽고, 수령하면서 첨부를 지급한다.
	//
	// 첨부는 콘솔 차트 MailItem 의 행이다(컬럼 RewardType = Item/Currency, TargetID). 수량은 발송 시 입력한 itemCount.
	// 받은 우편과 만료된 우편은 뒤끝이 목록에서 지운다 — 수령이 곧 삭제다.
	public class MailFunctions
	{
		// 한 번에 읽는 우편 수 — 종류별.
		private const int ListLimit = 100;

		public Stream MailList()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				List<MailDto> mails = new List<MailDto>();
				if (readPosts(MailPostType.Admin, mails, out string adminErr) == false)
				{
					return FuncResult.Error(adminErr);
				}

				if (readPosts(MailPostType.Rank, mails, out string rankErr) == false)
				{
					return FuncResult.Error(rankErr);
				}

				// ISO 8601 문자열은 사전순이 곧 시간순이다 — 최신순.
				mails.Sort(compareNewestFirst);

				MailListResponse response = new MailListResponse();
				response.success = true;
				response.mails = mails.ToArray();
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 수령은 되돌릴 수 없어 저장보다 먼저 한다 — 저장이 실패하면 보상은 사라지지만 중복 지급은 생기지 않는다.
		// 그 경우 에러에 우편과 첨부를 남겨 운영 보상의 근거로 쓴다.
		public Stream MailReceive()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				MailReceiveRequest req = JsonConvert.DeserializeObject<MailReceiveRequest>(Backend.Content["req"].ToString());
				if (req == null || (req.postType != MailPostType.Admin && req.postType != MailPostType.Rank))
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				// 첨부에 수집품(펫·코스튬)이 있을 수 있다.
				if (MyData.Load("USER_PET", out PetDto pet, out string petErr) == false)
				{
					return FuncResult.Error(petErr);
				}

				if (MyData.Load("USER_COSTUME", out CostumeDto costume, out string costumeErr) == false)
				{
					return FuncResult.Error(costumeErr);
				}

				PostType postType = toPostType(req.postType);
				bool receiveAll = string.IsNullOrEmpty(req.inDate);
				var receiveResult = receiveAll
					? Backend.UPost.ReceivePostItemAll(postType)
					: Backend.UPost.ReceivePostItem(postType, req.inDate);

				// 받을 우편이 없으면(모두 받기) 404 가 온다 — 빈 수령으로 본다.
				if (!receiveResult.IsSuccess())
				{
					if (receiveAll == true && receiveResult.GetStatusCode() == "404")
					{
						MailReceiveResponse empty = new MailReceiveResponse();
						empty.success = true;
						empty.rewards = new GrantedRewardDto[0];
						return FuncResult.Json(empty);
					}

					return FuncResult.Error("ReceivePostItem Failed: " + receiveResult.GetStatusCode() + " " + receiveResult.GetErrorCode());
				}

				string raw = receiveResult.GetReturnValue();
				JObject root = parseRaw(raw);
				JArray postItems = root["postItems"] as JArray;
				if (postItems == null)
				{
					return FuncResult.Error("postItems not found: " + raw);
				}

				// 단건은 [첨부...], 모두 받기는 [[첨부...], [첨부...]] 모양이다.
				List<MailAttachmentDto> attachments = new List<MailAttachmentDto>();
				for (int i = 0; i < postItems.Count; i++)
				{
					JArray nested = postItems[i] as JArray;
					if (nested != null)
					{
						readAttachments(nested, attachments);
					}
					else
					{
						addAttachment(postItems[i], attachments);
					}
				}

				List<RolledReward> rolled = new List<RolledReward>();
				for (int i = 0; i < attachments.Count; i++)
				{
					rolled.Add(toRolled(attachments[i]));
				}

				RewardApplier applier = new RewardApplier(inventory, currency, pet, costume);
				applier.ApplyAll(rolled);

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), DungeonProgressOps.ToParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), DungeonProgressOps.ToParam(currency)));

				if (applier.PetChanged == true)
				{
					tx.Add(TransactionValue.SetUpdate("USER_PET", new Where(), DungeonProgressOps.ToParam(pet)));
				}

				if (applier.CostumeChanged == true)
				{
					tx.Add(TransactionValue.SetUpdate("USER_COSTUME", new Where(), DungeonProgressOps.ToParam(costume)));
				}

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed after mail received: " + txResult.GetErrorCode()
						+ " postType " + req.postType + " inDate " + req.inDate + " items " + JsonConvert.SerializeObject(attachments));
				}

				MailReceiveResponse response = new MailReceiveResponse();
				response.success = true;
				response.rewards = applier.Granted;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static bool readPosts(int mailPostType, List<MailDto> mails, out string err)
		{
			var result = Backend.UPost.GetPostList(toPostType(mailPostType), ListLimit);
			if (!result.IsSuccess())
			{
				// 우편이 하나도 없으면 404 가 올 수 있다.
				if (result.GetStatusCode() == "404")
				{
					err = null;
					return true;
				}

				err = "GetPostList Failed: " + mailPostType + " " + result.GetStatusCode() + " " + result.GetErrorCode();
				return false;
			}

			string raw = result.GetReturnValue();
			JObject root = parseRaw(raw);
			JArray postList = root["postList"] as JArray;
			if (postList == null)
			{
				err = "postList not found: " + raw;
				return false;
			}

			for (int i = 0; i < postList.Count; i++)
			{
				JToken post = postList[i];

				MailDto mail = new MailDto();
				mail.postType = mailPostType;
				mail.inDate = readString(post, "inDate");
				mail.title = readString(post, "title");
				mail.content = readString(post, "content");
				mail.author = readString(post, "author");
				if (string.IsNullOrEmpty(mail.author) == true)
				{
					mail.author = readString(post, "rankType");
				}

				mail.sentDate = readString(post, "sentDate");
				mail.expirationDate = readString(post, "expirationDate");

				List<MailAttachmentDto> attachments = new List<MailAttachmentDto>();
				JArray items = post["items"] as JArray;
				if (items != null)
				{
					readAttachments(items, attachments);
				}

				mail.items = attachments.ToArray();
				mails.Add(mail);
			}

			err = null;
			return true;
		}

		private static void readAttachments(JArray items, List<MailAttachmentDto> attachments)
		{
			for (int i = 0; i < items.Count; i++)
			{
				addAttachment(items[i], attachments);
			}
		}

		// 첨부 1건 { item: { RewardType, TargetID, ... }, itemCount } — 차트 값은 문자열로 온다.
		// MailItem 차트가 아니거나 테이블에 없는 대상은 건너뛴다(지급하지 않는다).
		private static void addAttachment(JToken entry, List<MailAttachmentDto> attachments)
		{
			JToken item = entry["item"];
			if (item == null)
			{
				return;
			}

			RewardType rewardType;
			int targetId;
			int count;
			if (Enum.TryParse(readString(item, "RewardType"), out rewardType) == false
				|| int.TryParse(readString(item, "TargetID"), out targetId) == false
				|| int.TryParse(readString(entry, "itemCount"), out count) == false
				|| count <= 0)
			{
				return;
			}

			if (isValidTarget(rewardType, targetId) == false)
			{
				return;
			}

			MailAttachmentDto attachment = new MailAttachmentDto();
			attachment.rewardType = (int)rewardType;
			attachment.targetId = targetId;
			attachment.count = count;
			attachments.Add(attachment);
		}

		private static bool isValidTarget(RewardType rewardType, int targetId)
		{
			if (rewardType == RewardType.Currency)
			{
				return Enum.IsDefined(typeof(Currency), targetId);
			}

			if (rewardType == RewardType.Item)
			{
				Table_Item.Row row = Table_Item.Get(targetId);
				return row != null && row.MainCategory != ItemMainCategory.Equipment;
			}

			return false;
		}

		private static RolledReward toRolled(MailAttachmentDto attachment)
		{
			RolledReward rolled = new RolledReward();
			rolled.type = (RewardType)attachment.rewardType;
			rolled.count = attachment.count;
			if (rolled.type == RewardType.Currency)
			{
				rolled.currency = (Currency)attachment.targetId;
			}
			else
			{
				rolled.itemId = attachment.targetId;
			}

			return rolled;
		}

		private static PostType toPostType(int mailPostType)
		{
			return (mailPostType == MailPostType.Rank) ? PostType.Rank : PostType.Admin;
		}

		private static string readString(JToken token, string key)
		{
			JToken value = token[key];
			if (value == null || value.Type == JTokenType.Null)
			{
				return null;
			}

			return value.ToString();
		}

		// inDate 는 수령 요청에 원문 그대로 돌려줘야 한다 — 날짜 문자열을 DateTime 으로 바꾸지 않게 읽는다.
		private static JObject parseRaw(string raw)
		{
			JsonSerializerSettings settings = new JsonSerializerSettings();
			settings.DateParseHandling = DateParseHandling.None;
			return JsonConvert.DeserializeObject<JObject>(raw, settings);
		}

		private static int compareNewestFirst(MailDto a, MailDto b)
		{
			return string.CompareOrdinal(b.sentDate, a.sentDate);
		}
	}
}
