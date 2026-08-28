using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Field;
using ProjectOne.Flow;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// MainHud 의 Presenter — Account 조회와 이벤트 구독을 담당하고 View 에는 표시만 지시한다.
	public sealed class MainHudPresenter : Presenter<MainHud>
	{
		// 추적 중인 히어로. 씬마다 새로 스폰되므로 UnitSpawnedEvent 로 갈아끼운다.
		private UnitBase _hero;

		// 마지막으로 그린 체력 — 매 프레임 문자열을 새로 만들지 않기 위해 변화가 있을 때만 갱신한다.
		private float _lastHp = -1f;
		private float _lastMaxHp = -1f;

		protected override void OnInitialize()
		{
			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);

			view.OnScreenRequested += onScreenRequested;
			view.OnWarpRequested += onWarpRequested;

			refreshCharacter();

			// 부트 직후에는 어느 콘텐츠도 아니다 — 전부 숨긴 상태에서 시작한다.
			view.ApplyContext(HudContext.None);
		}

		protected override void OnDispose()
		{
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);

			if (view != null)
			{
				view.OnScreenRequested -= onScreenRequested;
				view.OnWarpRequested -= onWarpRequested;
			}

			if (_hero != null)
			{
				_hero.HpChanged -= onHpChanged;
			}

			_hero = null;
		}

		// 히어로의 체력/최대체력이 바뀐 프레임에 1회 불린다 (UnitBase.HpChanged).
		private void onHpChanged(UnitBase unit)
		{
			if (_hero == null || _hero.Vitals == null || _hero.Stats == null)
			{
				return;
			}

			float hp = _hero.Vitals.Hp;
			float maxHp = _hero.Stats.GetStat(Stat.Stat_MaxHp);

			if (hp == _lastHp && maxHp == _lastMaxHp)
			{
				return;
			}

			_lastHp = hp;
			_lastMaxHp = maxHp;
			view.SetHp(hp, maxHp);
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onCharacterChanged(CharacterChangeEvent e)
		{
			refreshCharacter();
		}

		private void onUnitSpawned(UnitSpawnedEvent e)
		{
			if (e.UnitType != UnitType.Hero)
			{
				return;
			}

			if (_hero != null)
			{
				_hero.HpChanged -= onHpChanged;
			}

			_hero = e.Unit;
			_lastHp = -1f;
			_lastMaxHp = -1f;

			if (_hero != null)
			{
				_hero.HpChanged += onHpChanged;
				onHpChanged(_hero);
			}
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

		// ── 이동 ──────────────────────────────────────────────────────

		// 목적지는 Table_Map.ID 하나로 들어온다. 어느 상태로 갈지는 Map 테이블의 MapType 이 정한다 —
		// 코드가 "이 버튼은 필드" 같은 분류표를 따로 들면 테이블과 두 벌이 되어 같이 틀어진다.
		private void onWarpRequested(int mapId)
		{
			Table_Map.Row map = Table_Map.Get(mapId);
			if (map == null)
			{
				Debug.LogWarning($"[MainHudPresenter] Map {mapId} 가 없습니다 — DevWarpButton 의 MapId 를 확인하세요.");
				return;
			}

			if (map.MapType == MapType.Town)
			{
				changeStateIfNeeded(new TownState(), typeof(TownState));
				return;
			}

			if (map.MapType == MapType.Field)
			{
				// Map.ID 와 Field.ID 는 같은 값이다.
				// 필드 밖(마을)이면 씬 전환, 필드 안이면 씬 전환 없이 이동/교체한다.
				if (FieldDirector.HasInstance == false)
				{
					GameFlow.Instance.ChangeStateAsync(new FieldState(mapId)).Forget();
					return;
				}

				FieldDirector.Instance.ChangeActAsync(mapId, view.GetCancellationTokenOnDestroy()).Forget();
				return;
			}

			Debug.LogWarning($"[MainHudPresenter] 아직 지원하지 않는 이동 대상입니다 — Map {mapId} ({map.MapType})");
		}

		// 같은 상태로 다시 들어가면 씬을 새로 로드해 히어로가 재스폰되고 위치가 초기화된다.
		private static void changeStateIfNeeded(IGameState next, System.Type stateType)
		{
			if (GameFlow.Instance.CurrentState != null && GameFlow.Instance.CurrentState.GetType() == stateType)
			{
				return;
			}

			GameFlow.Instance.ChangeStateAsync(next).Forget();
		}

		// ── 표시 ──────────────────────────────────────────────────────

		private void refreshCharacter()
		{
			Loadout loadout = Account.Instance.Loadout;

			view.SetLevel(loadout.Level);
			view.SetExp(loadout.Exp, requiredExpForNext(loadout.Level));
		}

		// 다음 레벨 도달에 필요한 누적 경험치. 없으면 0(최대 레벨).
		private static int requiredExpForNext(int level)
		{
			Table_CharacterLevelExp.Row next = Table_CharacterLevelExp.Get(level + 1);
			return next != null ? next.TotalExperience : 0;
		}
	}
}
