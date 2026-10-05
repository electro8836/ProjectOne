using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Mail;
using ProjectOne.Map;

namespace ProjectOne.UI
{
	// MainHud 의 Presenter — 이벤트 구독과 버프 목록 조회를 담당하고 View 에는 표시만 지시한다.
	// 체력·레벨·전투력은 HeroInfo 가 자기 캔버스에서 따로 그린다.
	public sealed class MainHudPresenter : Presenter<MainHud>
	{
		protected override void OnInitialize()
		{
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Subscribe<MailChangedEvent>(onMailChanged);

			view.OnScreenRequested += onScreenRequested;
			view.OnWarpRequested += onWarpRequested;
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
				view.OnScreenRequested -= onScreenRequested;
				view.OnWarpRequested -= onWarpRequested;
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

		// ── 화면 열기 ─────────────────────────────────────────────────

		// NPC 클릭과 같은 진입점을 쓴다 — "굳이 NPC 에게 가지 않아도 같은 창이 열린다".
		private void onScreenRequested(UIScreenId id)
		{
			if (id == UIScreenId.None)
			{
				Debug.LogWarning("[MainHudPresenter] 화면이 지정되지 않은 버튼이 눌렸습니다 — ScreenOpenButton 의 Screen 을 확인하세요.");
				return;
			}

			openAsync(id).Forget();
		}

		private async UniTaskVoid openAsync(UIScreenId id)
		{
			await UIManager.Instance.OpenAsync(id, view.GetCancellationTokenOnDestroy());
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
			else if (result == MenuPopupResult.Setting)
			{
				// 설정 팝업이 생기기 전까지는 닉네임 변경 팝업을 바로 연다.
				await UIManager.Instance.ShowNicknamePopupAsync(ct);
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

		// ── 이동 ──────────────────────────────────────────────────────

		// 목적지는 Table_Map.ID 하나로 들어온다. 어느 상태로 갈지는 MapNavigator 가 Map 테이블을 보고 정한다 —
		// 월드 화면의 필드 이동도 같은 판단을 하므로 분기를 두 벌로 두지 않는다.
		private void onWarpRequested(int mapId)
		{
			MapNavigator.MoveToMap(mapId, view.GetCancellationTokenOnDestroy());
		}
	}
}
