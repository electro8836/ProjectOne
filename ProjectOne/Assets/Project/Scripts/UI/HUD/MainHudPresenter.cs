using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
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
			}

			_hero = null;
		}

		// View 의 Update 가 위임한다. 체력은 이벤트가 없어 값 비교로 갱신한다 —
		// 피격마다 이벤트를 쏘면 다단히트에서 프레임당 수십 번 그리게 된다.
		public void Tick()
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

			_hero = e.Unit;
			_lastHp = -1f;
			_lastMaxHp = -1f;
		}

		// 상태 전이가 곧 맥락 전환이다. 어떤 버튼이 보일지는 View 인스펙터가 정한다.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			view.ApplyContext(toContext(e.StateType));
		}

		private static HudContext toContext(System.Type stateType)
		{
			if (stateType == typeof(TownState))
			{
				return HudContext.Town;
			}

			if (stateType == typeof(FieldState))
			{
				return HudContext.Field;
			}

			if (stateType == typeof(DungeonState))
			{
				return HudContext.Dungeon;
			}

			// 타이틀·패치·로딩 등 — HUD 를 보일 자리가 아니다.
			return HudContext.None;
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
