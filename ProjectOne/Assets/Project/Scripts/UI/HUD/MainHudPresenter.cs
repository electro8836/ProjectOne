using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Buff;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// MainHud 의 Presenter — 이벤트 구독과 버프 목록 조회를 담당하고 View 에는 표시만 지시한다.
	// 체력·레벨·전투력은 HeroInfo 가 자기 캔버스에서 따로 그린다.
	public sealed class MainHudPresenter : Presenter<MainHud>
	{
		// 추적 중인 히어로. 씬마다 새로 스폰되므로 UnitSpawnedEvent 로 갈아끼운다.
		// 버프 목록을 읽기 위한 참조다 — 체력·레벨·전투력은 HeroInfo 가 따로 본다.
		private UnitBase _hero;

		// 버프는 추가·갱신 이벤트가 없어(BuffContainer 에는 BuffRemoved 뿐) 주기 폴링으로 읽는다.
		// 남은 시간도 매 프레임 줄어드는 값이라 어차피 주기적으로 다시 봐야 한다.
		private const float BuffRefreshInterval = 0.1f;

		private IntervalTimer _buffTimer;
		private readonly List<BuffSlotData> _buffData = new List<BuffSlotData>();

		// 같은 ID 중복 제거용. Independent 정책 버프는 인스턴스마다 목록에 한 번씩 나온다.
		private readonly List<EDT.Buff> _buffSeen = new List<EDT.Buff>();

		protected override void OnInitialize()
		{
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);

			view.OnScreenRequested += onScreenRequested;
			view.OnWarpRequested += onWarpRequested;
			view.OnMenuRequested += onMenuRequested;

			// 부트 직후에는 어느 콘텐츠도 아니다 — 전부 숨긴 상태에서 시작한다.
			view.ApplyContext(HudContext.None);
		}

		protected override void OnDispose()
		{
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);

			if (view != null)
			{
				view.OnScreenRequested -= onScreenRequested;
				view.OnWarpRequested -= onWarpRequested;
				view.OnMenuRequested -= onMenuRequested;
			}

			_hero = null;
		}

		// View 의 Update 가 매 프레임 넘긴다. 주기가 찼을 때만 버프를 다시 읽는다.
		public void Tick(float dt)
		{
			if (_buffTimer.Tick(dt, BuffRefreshInterval) <= 0)
			{
				return;
			}

			refreshBuffs();
		}

		// 히어로에게 걸린 버프를 슬롯 데이터로 옮긴다. 디버프는 싣지 않는다.
		private void refreshBuffs()
		{
			_buffData.Clear();

			// 스폰 전에는 컨테이너가 없다 — 빈 목록으로 슬롯을 전부 접는다.
			if (_hero == null || _hero.BuffContainer == null)
			{
				view.RenderBuffs(_buffData);
				return;
			}

			BuffContainer container = _hero.BuffContainer;

			// GetActive 가 돌려주는 것은 컨테이너 내부의 재사용 버퍼다 — 들고 있지 말고 그 자리에서 옮긴다.
			IReadOnlyList<EDT.Buff> active = container.GetActive();

			_buffSeen.Clear();

			for (int i = 0; i < active.Count; i++)
			{
				EDT.Buff id = active[i];
				if (_buffSeen.Contains(id) == true)
				{
					continue;
				}

				_buffSeen.Add(id);

				BuffRuntime runtime = container.GetRuntime(id);
				if (runtime == null || runtime.IsDebuff == true)
				{
					continue;
				}

				Table_Buff.Row row = Table_Buff.Get(id);

				BuffSlotData data;
				data.buff = id;
				data.iconAddress = (row != null) ? row.Icon : string.Empty;

				// 중첩 합산은 컨테이너가 한다 — Independent 는 인스턴스 개수, 나머지는 대표의 Stack.
				data.stack = container.GetStack(id);
				data.remainingSeconds = runtime.RemainingDuration;
				data.isInfinite = runtime.IsInfinite;

				_buffData.Add(data);
			}

			view.RenderBuffs(_buffData);
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onUnitSpawned(UnitSpawnedEvent e)
		{
			if (e.UnitType != UnitType.Hero)
			{
				return;
			}

			_hero = e.Unit;
		}

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
