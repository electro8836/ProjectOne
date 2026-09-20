using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;
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

			view.OnScreenRequested += onScreenRequested;
			view.OnWarpRequested += onWarpRequested;
			view.OnMenuRequested += onMenuRequested;

			// 부트 직후에는 어느 콘텐츠도 아니다 — 전부 숨긴 상태에서 시작한다.
			view.ApplyContext(HudContext.None);
		}

		protected override void OnDispose()
		{
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);

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

		private async UniTaskVoid openMenuAsync()
		{
			await UIManager.Instance.ShowMenuPopupAsync(view.GetCancellationTokenOnDestroy());
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
