using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Event;
using ProjectOne.Mail;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 메일함 팝업 Presenter — 메일 목록을 받아 그리고, 메일 열기와 모두 수령을 처리한다.
	public sealed class MailBoxPopupPresenter : Presenter<MailBoxPopup>
	{
		private readonly IMailProvider _provider = MailSystem.Provider;

		protected override void OnInitialize()
		{
			view.OnMailSelected += onMailSelected;
			view.OnAllClaimRequested += onAllClaimRequested;
		}

		protected override void OnDispose()
		{
			view.OnMailSelected -= onMailSelected;
			view.OnAllClaimRequested -= onAllClaimRequested;
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			bool cancelled = await refreshAsync(ct);
			if (cancelled == true)
			{
				return;
			}

			view.Reveal();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 취소됐으면 true.
		private async UniTask<bool> refreshAsync(CancellationToken ct)
		{
			(bool cancelled, IReadOnlyList<MailData> mails) = await _provider.GetMailsAsync(ct).SuppressCancellationThrow();
			if (cancelled == true || view == null)
			{
				return true;
			}

			view.RenderMails(mails);

			// 열기·수령·삭제·모두 수령이 모두 여기를 지난다 — 배지 갱신 알림은 이 한 곳에서 낸다.
			EventManager.Instance.Publish(new MailChangedEvent(MailSystem.HasUnread(mails)));
			return false;
		}

		private void onMailSelected(MailData mail)
		{
			openMailAsync(mail, view.GetDestroyToken()).Forget();
		}

		// 메일 팝업은 이 팝업 위에 뜬다 — 닫히면 삭제·수령 결과를 목록에 다시 그린다.
		private async UniTaskVoid openMailAsync(MailData mail, CancellationToken ct)
		{
			// 열람 기록은 수령·삭제와 별개다 — 여는 순간 남기고 바로 Dim 을 켠다.
			MailReadLog.MarkRead(mail.id);
			if (await refreshAsync(ct) == true)
			{
				return;
			}

			bool cancelled = await UIManager.Instance.ShowMailSlotPopupAsync(mail, _provider, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			await refreshAsync(ct);
		}

		private void onAllClaimRequested()
		{
			claimAllAsync(view.GetDestroyToken()).Forget();
		}

		// 모든 메일이 지워져 되돌릴 수 없으므로 한 번 더 확인받는다.
		private async UniTaskVoid claimAllAsync(CancellationToken ct)
		{
			CommonPopupData data;
			data.title = "모두 수령 및 삭제";
			data.desc = "모든 보상을 수령하고 모든 우편을 삭제 합니다.\n계속하시겠습니까?";
			data.button1Text = "아니오";
			data.button2Text = "예";

			// 아니오·닫기·Dim 은 팝업만 닫는다.
			(bool confirmCancelled, CommonPopupResult result) = await UIManager.Instance.ShowCommonPopupAsync(data, ct).SuppressCancellationThrow();
			if (confirmCancelled == true || result != CommonPopupResult.Button2)
			{
				return;
			}

			(bool cancelled, List<GrantedReward> rewards) = await _provider.ClaimAndDeleteAllAsync(ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			if (await refreshAsync(ct) == true)
			{
				return;
			}

			// 첨부가 하나도 없었으면 ShowRewardPopupAsync 가 아무것도 띄우지 않는다.
			await UIManager.Instance.ShowRewardPopupAsync(rewards, ct).SuppressCancellationThrow();
		}
	}
}
