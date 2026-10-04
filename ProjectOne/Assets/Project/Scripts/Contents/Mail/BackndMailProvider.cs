using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Network;
using ProjectOne.Reward;
using ProjectOne.Shared;

namespace ProjectOne.Mail
{
	// 뒤끝 우편(관리자·랭킹). 목록은 서버가 읽어 주고, 수령은 서버가 받으면서 첨부를 지급한다.
	//
	// 뒤끝은 받은 우편을 서버 목록에서 지운다 — 받은 메일은 기기 보관함(MailArchive)에 남겨 함께 보여 준다.
	// 메일함 = 서버 미수령 우편 + 보관함(claimed). 삭제는 보관함에서만 한다.
	// 메일함은 열기·수령마다 목록을 다시 그리므로, 서버 목록은 잠시 캐시한다.
	public sealed class BackndMailProvider : IMailProvider
	{
		// 서버 목록 캐시 유효 시간 — 콘솔에서 새로 보낸 우편은 이 시간이 지나야 보인다.
		private const float CacheSeconds = 60f;

		private const string AdminSender = "운영팀";
		private const string RankSender = "랭킹 보상";

		// 서버 미수령 우편 — 마지막 목록 응답
		private readonly List<MailDto> _serverMails = new List<MailDto>();

		// 화면 목록과 메일 id → 원본. 팝업이 MailData 를 붙잡고 있으므로 다시 만들 때 같은 id 는 같은 객체를 쓴다.
		private readonly List<MailData> _mails = new List<MailData>();
		private readonly Dictionary<string, MailDto> _sources = new Dictionary<string, MailDto>();

		private float _fetchedAt = float.NegativeInfinity;

		// 같은 action 은 응답 전 중복 호출이 버려진다(BackndFunctionCaller) — 동시에 들어온 목록 요청은 한 응답을 나눠 기다린다.
		private UniTaskCompletionSource<IReadOnlyList<MailData>> _listTcs;

		public UniTask<IReadOnlyList<MailData>> GetMailsAsync(CancellationToken ct)
		{
			if (Time.realtimeSinceStartup - _fetchedAt < CacheSeconds)
			{
				rebuild();
				return UniTask.FromResult<IReadOnlyList<MailData>>(_mails);
			}

			if (_listTcs == null)
			{
				_listTcs = new UniTaskCompletionSource<IReadOnlyList<MailData>>();
				NetworkManager.Instance.RequestMailList(onListReceived);
			}

			return _listTcs.Task.AttachExternalCancellation(ct);
		}

		public void MarkStale()
		{
			_fetchedAt = float.NegativeInfinity;
		}

		// 받으면 보관함으로 옮기고 claimed 로 표시한다 — 목록에는 남는다.
		public async UniTask<List<GrantedReward>> ClaimAsync(string mailId, CancellationToken ct)
		{
			List<GrantedReward> rewards = new List<GrantedReward>();

			MailDto source;
			if (_sources.TryGetValue(mailId, out source) == false || MailArchive.Contains(source.postType, source.inDate) == true)
			{
				return rewards;
			}

			bool received = await receiveAsync(source.postType, source.inDate, rewards).AttachExternalCancellation(ct);
			if (received == true)
			{
				_serverMails.Remove(source);
				MailArchive.Add(source);
				rebuild();
			}

			return rewards;
		}

		// 보관함에 있으면 기기에서만 지운다. 아직 받지 않은 메일(메시지 메일 수령 실패 등)은 서버에서 받아 지우고 보관하지 않는다.
		public async UniTask DeleteAsync(string mailId, CancellationToken ct)
		{
			MailDto source;
			if (_sources.TryGetValue(mailId, out source) == false)
			{
				return;
			}

			if (MailArchive.Contains(source.postType, source.inDate) == true)
			{
				MailArchive.Remove(source.postType, source.inDate);
				rebuild();
				return;
			}

			List<GrantedReward> rewards = new List<GrantedReward>();
			bool received = await receiveAsync(source.postType, source.inDate, rewards).AttachExternalCancellation(ct);
			if (received == true)
			{
				_serverMails.Remove(source);
				MailReadLog.Clear(mailId);
				rebuild();
			}
		}

		// 남은 보상을 모두 받고 보관함까지 비운다 — 종류별로, 같은 action 이라 순서대로 보낸다.
		public async UniTask<List<GrantedReward>> ClaimAndDeleteAllAsync(CancellationToken ct)
		{
			List<GrantedReward> rewards = new List<GrantedReward>();

			bool adminReceived = await receiveAsync(MailPostType.Admin, null, rewards).AttachExternalCancellation(ct);
			bool rankReceived = await receiveAsync(MailPostType.Rank, null, rewards).AttachExternalCancellation(ct);
			if (adminReceived == true && rankReceived == true)
			{
				for (int i = 0; i < _serverMails.Count; i++)
				{
					MailReadLog.Clear(MailArchive.ToMailId(_serverMails[i]));
				}

				_serverMails.Clear();
				MailArchive.Clear();
				rebuild();
			}
			else
			{
				// 한쪽이 실패했으면 무엇이 남았는지 서버에서 다시 받는다. 보관함은 그대로 둔다.
				_fetchedAt = float.NegativeInfinity;
			}

			return rewards;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void onListReceived(bool success, MailListResponse data, string error)
		{
			UniTaskCompletionSource<IReadOnlyList<MailData>> tcs = _listTcs;
			_listTcs = null;

			// 실패하면 이전 서버 목록을 그대로 쓴다(실패 안내는 4단계).
			if (success == false || data == null || data.mails == null)
			{
				Debug.LogWarning($"[BackndMailProvider] 우편 목록 조회 실패: {error}");
			}
			else
			{
				_serverMails.Clear();
				_serverMails.AddRange(data.mails);
				_fetchedAt = Time.realtimeSinceStartup;
			}

			rebuild();

			// 서버 목록이 바뀌는 유일한 지점 — 메뉴 팝업·새 우편 알림으로 받아도 메뉴 버튼 배지가 같이 바뀐다.
			EventManager.Instance.Publish(new MailChangedEvent(MailSystem.HasUnread(_mails)));
			tcs.TrySetResult(_mails);
		}

		// 서버 미수령 + 보관함을 최신순으로 합친다. 이미 있던 id 는 같은 MailData 를 다시 쓴다.
		private void rebuild()
		{
			Dictionary<string, MailData> previous = new Dictionary<string, MailData>();
			for (int i = 0; i < _mails.Count; i++)
			{
				previous[_mails[i].id] = _mails[i];
			}

			_mails.Clear();
			_sources.Clear();

			for (int i = 0; i < _serverMails.Count; i++)
			{
				MailDto dto = _serverMails[i];
				if (MailArchive.Contains(dto.postType, dto.inDate) == true)
				{
					continue;
				}

				addMail(dto, false, previous);
			}

			IReadOnlyList<MailDto> archived = MailArchive.All;
			for (int i = 0; i < archived.Count; i++)
			{
				addMail(archived[i], true, previous);
			}

			_mails.Sort(compareNewestFirst);
		}

		private void addMail(MailDto dto, bool claimed, Dictionary<string, MailData> previous)
		{
			string id = MailArchive.ToMailId(dto);
			MailData mail;
			if (previous.TryGetValue(id, out mail) == false)
			{
				mail = toMailData(id, dto);
			}

			mail.claimed = claimed;
			_mails.Add(mail);
			_sources[id] = dto;
		}

		private static int compareNewestFirst(MailData a, MailData b)
		{
			return b.sentAt.CompareTo(a.sentAt);
		}

		// 받았으면 true. 지급분은 로컬에 반영하고 rewards 에 덧붙인다.
		private static UniTask<bool> receiveAsync(int postType, string inDate, List<GrantedReward> rewards)
		{
			UniTaskCompletionSource<bool> tcs = new UniTaskCompletionSource<bool>();
			ReceiveHandler handler = new ReceiveHandler(tcs, rewards);

			MailReceiveRequest request = new MailReceiveRequest();
			request.postType = postType;
			request.inDate = inDate;
			NetworkManager.Instance.RequestMailReceive(request, handler.OnReceived);

			return tcs.Task;
		}

		private static MailData toMailData(string id, MailDto dto)
		{
			MailData mail = new MailData();
			mail.id = id;
			mail.title = dto.title;
			mail.body = dto.content;
			mail.sender = string.IsNullOrEmpty(dto.author) == false ? dto.author
				: (dto.postType == MailPostType.Rank ? RankSender : AdminSender);
			mail.sentAt = parseDate(dto.sentDate);

			if (dto.items != null)
			{
				for (int i = 0; i < dto.items.Length; i++)
				{
					MailAttachmentDto src = dto.items[i];
					RewardPreviewItem item = default(RewardPreviewItem);
					item.type = (RewardType)src.rewardType;
					item.count = src.count;
					if (item.type == RewardType.Currency)
					{
						item.currency = (EDT.Currency)src.targetId;
					}
					else
					{
						item.itemId = src.targetId;
					}

					mail.attachments.Add(item);
				}
			}

			return mail;
		}

		// 서버는 UTC ISO 8601 을 보낸다 — 화면에는 기기 현지 시각으로.
		private static DateTime parseDate(string value)
		{
			DateTime parsed;
			if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed) == false)
			{
				return DateTime.MinValue;
			}

			return parsed.ToLocalTime();
		}

		// 수령 응답 1건의 처리 — 콜백을 메서드 그룹으로 넘기기 위해 상태를 묶는다.
		private sealed class ReceiveHandler
		{
			private readonly UniTaskCompletionSource<bool> _tcs;
			private readonly List<GrantedReward> _rewards;

			public ReceiveHandler(UniTaskCompletionSource<bool> tcs, List<GrantedReward> rewards)
			{
				_tcs = tcs;
				_rewards = rewards;
			}

			public void OnReceived(bool success, MailReceiveResponse data, string error)
			{
				if (success == false || data == null)
				{
					Debug.LogWarning($"[BackndMailProvider] 우편 수령 실패: {error}");
					_tcs.TrySetResult(false);
					return;
				}

				int start = _rewards.Count;
				RewardGranter.FromServer(data.rewards, null, _rewards);
				RewardGranter.ApplyRange(_rewards, start);
				_tcs.TrySetResult(true);
			}
		}
	}
}
