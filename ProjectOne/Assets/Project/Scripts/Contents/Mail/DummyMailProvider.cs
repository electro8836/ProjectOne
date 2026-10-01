using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;

namespace ProjectOne.Mail
{
	// [임시] 뒤끝 우편이 생기기 전까지 쓰는 가짜 메일.
	//
	// 목록을 static 으로 들고 있어 팝업을 닫았다 열어도 수령·삭제 상태가 유지되고, 앱을 다시 켜면 처음 상태로 돌아온다.
	// 수령은 화면 표시용 목록만 만든다 — 인벤/지갑에 손대지 않는다(지급은 서버 권위).
	public sealed class DummyMailProvider : IMailProvider
	{
		// 아이템 첨부 메일에 쓸 보상 그룹 — 몬스터 드랍 테스트 그룹(골드·주문서·물약)을 빌려 쓴다.
		private const int DummyItemRewardGroupId = 9001;

		private static List<MailData> _mails;

		public UniTask<IReadOnlyList<MailData>> GetMailsAsync(CancellationToken ct)
		{
			ensureMails();
			return UniTask.FromResult<IReadOnlyList<MailData>>(_mails);
		}

		public UniTask<List<GrantedReward>> ClaimAsync(string mailId, CancellationToken ct)
		{
			ensureMails();

			List<GrantedReward> rewards = new List<GrantedReward>();
			MailData mail = find(mailId);
			if (mail != null)
			{
				claim(mail, rewards);
			}

			return UniTask.FromResult(rewards);
		}

		public UniTask DeleteAsync(string mailId, CancellationToken ct)
		{
			ensureMails();

			MailData mail = find(mailId);
			if (mail != null)
			{
				_mails.Remove(mail);
			}

			return UniTask.CompletedTask;
		}

		public UniTask<List<GrantedReward>> ClaimAndDeleteAllAsync(CancellationToken ct)
		{
			ensureMails();

			List<GrantedReward> rewards = new List<GrantedReward>();
			for (int i = 0; i < _mails.Count; i++)
			{
				claim(_mails[i], rewards);
			}

			_mails.Clear();
			return UniTask.FromResult(rewards);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void claim(MailData mail, List<GrantedReward> rewards)
		{
			if (mail.HasAttachments == false || mail.claimed == true)
			{
				return;
			}

			for (int i = 0; i < mail.attachments.Count; i++)
			{
				RewardPreviewItem item = mail.attachments[i];

				GrantedReward reward;
				reward.type = item.type;
				reward.itemId = item.itemId;
				reward.currency = item.currency;
				reward.count = item.count;
				reward.equipment = item.equipment;
				rewards.Add(reward);
			}

			mail.claimed = true;
		}

		private static MailData find(string mailId)
		{
			for (int i = 0; i < _mails.Count; i++)
			{
				if (_mails[i].id == mailId)
				{
					return _mails[i];
				}
			}

			return null;
		}

		// 최신순으로 쌓는다 — GetMailsAsync 가 그대로 돌려준다.
		private static void ensureMails()
		{
			if (_mails != null)
			{
				return;
			}

			_mails = new List<MailData>();

			MailData shop = createMail("dummy_mail_5", "양대용", "온라인 쇼핑몰 주문 번호 #1234567890", new DateTime(2026, 3, 6, 12, 0, 0),
				"주문하신 상품이 도착했습니다.\n아래 첨부된 상품을 수령해 주세요.");
			addCurrency(shop, EDT.Currency.Dia, 1000);
			_mails.Add(shop);

			MailData eventReward = createMail("dummy_mail_4", "운영팀", "이벤트 참여 보상", new DateTime(2026, 3, 5, 18, 30, 0),
				"이벤트에 참여해 주셔서 감사합니다.\n보상을 지급해 드립니다.");
			RewardPreview.Build(DummyItemRewardGroupId, eventReward.attachments);
			_mails.Add(eventReward);

			MailData notice = createMail("dummy_mail_3", "운영팀", "정기 점검 안내", new DateTime(2026, 3, 4, 9, 0, 0),
				"3월 10일 04:00 ~ 08:00 정기 점검이 진행됩니다.\n점검 중에는 게임에 접속할 수 없습니다.");
			_mails.Add(notice);

			MailData compensation = createMail("dummy_mail_2", "운영팀", "점검 보상", new DateTime(2026, 2, 28, 14, 25, 0),
				"점검 연장으로 불편을 드려 죄송합니다.\n보상을 확인해 주세요.");
			addCurrency(compensation, EDT.Currency.Gold, 10000);
			addCurrency(compensation, EDT.Currency.Dia, 300);
			_mails.Add(compensation);

			MailData welcome = createMail("dummy_mail_1", "운영팀", "모험가님, 환영합니다!", new DateTime(2026, 1, 1, 0, 0, 0),
				"게임을 시작해 주셔서 감사합니다.\n즐거운 모험 되세요!");
			_mails.Add(welcome);
		}

		private static MailData createMail(string id, string sender, string title, DateTime sentAt, string body)
		{
			MailData mail = new MailData();
			mail.id = id;
			mail.sender = sender;
			mail.title = title;
			mail.sentAt = sentAt;
			mail.body = body;
			return mail;
		}

		private static void addCurrency(MailData mail, EDT.Currency currency, int count)
		{
			RewardPreviewItem item = default(RewardPreviewItem);
			item.type = RewardType.Currency;
			item.currency = currency;
			item.count = count;
			mail.attachments.Add(item);
		}
	}
}
