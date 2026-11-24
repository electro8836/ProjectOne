using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Mail;

namespace ProjectOne.UI
{
	// MainHud 의 Presenter — 맥락 전환·메뉴 배지·메뉴 팝업을 맡고 View 에는 표시만 지시한다.
	// 체력·버프 등 자기 이벤트를 받는 위젯은 MainHud 의 Hud* 클래스가 따로 그린다.
	public sealed class MainHudPresenter : Presenter<MainHud>
	{
		protected override void OnInitialize()
		{
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Subscribe<MailChangedEvent>(onMailChanged);

			view.OnMenuRequested += onMenuRequested;

			// 부트 직후에는 어느 콘텐츠도 아니다 — 전부 숨긴 상태에서 시작한다.
			view.ApplyContext(HudContext.None);

			refreshMenuBadgeAsync().Forget();
		}

		protected override void OnDispose()
		{
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Unsubscribe<MailChangedEvent>(onMailChanged);

			if (view != null)
			{
				view.OnMenuRequested -= onMenuRequested;
			}
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 상태 전이가 곧 맥락 전환이다. 어떤 버튼이 보일지는 View 인스펙터가 정한다.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			view.ApplyContext(HudContexts.FromState(e.StateType));
		}

		// 메뉴 배지는 지금은 미확인 메일 여부와 같다. 다른 알림이 생기면 여기서 OR 로 합친다.
		private void onMailChanged(MailChangedEvent e)
		{
			view.SetMenuBadge(e.HasUnread);
		}

		// HUD 가 처음 설 때 한 번 — 그 뒤로는 메일함이 내는 MailChangedEvent 로 갱신된다.
		private async UniTaskVoid refreshMenuBadgeAsync()
		{
			bool hasUnread = await MailSystem.HasUnreadAsync(view.GetCancellationTokenOnDestroy());
			if (view == null)
			{
				return;
			}

			view.SetMenuBadge(hasUnread);
		}

		// ── 메뉴 팝업 ─────────────────────────────────────────────────

		private void onMenuRequested()
		{
			openMenuAsync().Forget();
		}

		// 메뉴가 닫힌 뒤 고른 항목의 팝업을 띄운다.
		private async UniTaskVoid openMenuAsync()
		{
			CancellationToken ct = view.GetCancellationTokenOnDestroy();
			MenuPopupResult result = await UIManager.Instance.ShowMenuPopupAsync(ct);

			if (result == MenuPopupResult.Mail)
			{
				await UIManager.Instance.ShowMailBoxPopupAsync(ct);
			}
			else if (result == MenuPopupResult.Quit)
			{
				await confirmQuitAsync(ct);
			}
			else if (result == MenuPopupResult.Account)
			{
				await UIManager.Instance.ShowAccountPopupAsync(ct);
			}
			else if (result == MenuPopupResult.Ranking)
			{
				await UIManager.Instance.ShowRankingPopupAsync(ct);
			}
			else if (result == MenuPopupResult.HeroPass)
			{
				await UIManager.Instance.ShowHeroPassPopupAsync(ct);
			}
			else if (result == MenuPopupResult.DailyBonus)
			{
				await UIManager.Instance.ShowDailyBonusPopupAsync(ct);
			}
			else if (result == MenuPopupResult.Quest)
			{
				await UIManager.Instance.ShowQuestListPopupAsync(ct);
			}
			else if (result == MenuPopupResult.Map)
			{
				await UIManager.Instance.OpenMapScreenAsync(ct);
			}
		}

		// 잘못 눌러 꺼지지 않도록 한 번 더 확인받는다.
		private async UniTask confirmQuitAsync(CancellationToken ct)
		{
			CommonPopupData data;
			data.title = "게임 종료";
			data.desc = "게임을 종료하시겠습니까?";
			data.button1Text = "아니오";
			data.button2Text = "예";
			data.isDanger = false;

			// 아니오·닫기·Dim 은 팝업만 닫는다.
			(bool cancelled, CommonPopupResult result) = await UIManager.Instance.ShowCommonPopupAsync(data, ct).SuppressCancellationThrow();
			if (cancelled == true || result != CommonPopupResult.Button2)
			{
				return;
			}

			quitGame();
		}

		// 에디터에서는 플레이 모드를 유지한 채 로그만 남기고, 빌드에서는 앱을 종료한다.
		private void quitGame()
		{
#if UNITY_EDITOR
			Debug.Log("[MainHudPresenter] 게임 종료 요청 (에디터에서는 종료하지 않음)");
#else
			Application.Quit();
#endif
		}
	}
}
