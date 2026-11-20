using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 메인 HUD 보스 배너.
	// - 보스 몬스터(Table_Monster.MonsterType == Boss) 등장 시 표시
	// - 체력은 UnitBase.HpChanged 로 갱신 (게이지 / 현재·최대 수치 / 퍼센트)
	// - 버프 상태(피해 면역·브레이크 기절)는 부착/해제 이벤트가 없어 Tick 에서 폴링한다
	// - 보스 사망 시 DeathHoldMs 유지 후 USS 전환으로 1초 동안 사라진다
	// - 떠 있는 동안 HUD 루트에 hud--boss 를 붙여 같은 자리를 쓰는 미니맵·퀘스트를 숨긴다
	public sealed class HudBossBanner
	{
		private const long DeathHoldMs = 3000;
		private const long FadeMs = 1000;

		private readonly VisualElement _hud;
		private readonly VisualElement _banner;
		private readonly Label _name;
		private readonly Label _level;
		private readonly VisualElement _hpFill;
		private readonly Label _hpText;
		private readonly Label _hpPercent;
		private readonly VisualElement _breakRow;
		private readonly VisualElement _breakFill;
		private readonly Label _state;

		private UnitBase _boss;
		// 보스의 브레이크 컴포넌트 — 스폰 시 1회만 잡는다(풀 개체라 수명 내내 유지된다).
		private MonsterBreak _break;
		private IVisualElementScheduledItem _fadeStep;
		private bool _showing;

		private float _lastHp = -1f;
		private float _lastMaxHp = -1f;
		private int _lastPercent = -1;
		private bool _lastInvincible;
		private bool _lastBreakStun;
		private bool _stateInitialized;
		private float _lastBreakRatio = -1f;

		// 배너가 화면에 떠 있는가. 던전 웨이브 배너가 자리를 비켜줄지 판단하는 근거다(보스 우선).
		public bool IsShowing
		{
			get { return _showing; }
		}

		public HudBossBanner(VisualElement root)
		{
			_hud = root.Q("hud");
			_banner = root.Q("boss");
			_name = root.Q<Label>("boss-name");
			_level = root.Q<Label>("boss-level");
			_hpFill = root.Q("boss-hp-fill");
			_hpText = root.Q<Label>("boss-hp-text");
			_hpPercent = root.Q<Label>("boss-hp-percent");
			_breakRow = root.Q("boss-break-row");
			_breakFill = root.Q("boss-break-fill");
			_state = root.Q<Label>("boss-state");

			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);
			EventManager.Instance.Subscribe<UnitDiedEvent>(onUnitDied);
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(onUnitDied);

			if (_boss != null)
			{
				_boss.HpChanged -= onBossHpChanged;
			}

			_boss = null;
		}

		public void Tick()
		{
			// 참조 자체가 비어 있을 때만 건너뛴다 — == null 은 파괴된 보스에도 true 라 아래 걷기 경로를 막는다.
			if (ReferenceEquals(_boss, null) == true)
			{
				return;
			}

			// 맵 이동·스테이지 정리(풀 반환)·씬 전환(풀 파괴)은 사망이 아니라 UnitDiedEvent 가 나가지 않는다.
			// 배너가 영영 남으므로 여기서 직접 걷는다 — 보스가 이미 없으니 페이드 없이 즉시 감춘다.
			if (_boss == null || _boss.isActiveAndEnabled == false)
			{
				detachBoss();
				hide();
				return;
			}

			refreshState();
		}

		// 보스 참조를 끊고 표시를 일반 상태로 되돌린다. 사망과 풀 회수가 공유한다.
		private void detachBoss()
		{
			if (_boss != null)
			{
				_boss.HpChanged -= onBossHpChanged;
			}

			_boss = null;
			_break = null;
			_stateInitialized = false;
			applyStateVisual(false, false);
		}

		private void onBossHpChanged(UnitBase unit)
		{
			refreshHp();
		}

		private void refreshHp()
		{
			if (_boss == null)
			{
				return;
			}

			float max = _boss.Stats.GetStat(Stat.Stat_MaxHp);
			float hp = _boss.Vitals.Hp;
			if (hp == _lastHp && max == _lastMaxHp)
			{
				return;
			}

			_lastHp = hp;
			_lastMaxHp = max;

			float ratio = (max > 0f) ? Mathf.Clamp01(hp / max) : 0f;
			_hpFill.style.width = Length.Percent(ratio * 100f);
			_hpText.text = Mathf.CeilToInt(hp).ToString("N0") + " / " + Mathf.CeilToInt(max).ToString("N0");

			int percent = Mathf.RoundToInt(ratio * 100f);
			if (percent != _lastPercent)
			{
				_lastPercent = percent;
				_hpPercent.text = percent.ToString() + "%";
			}
		}

		// 상태가 바뀐 프레임에만 클래스·문구를 바꾼다.
		private void refreshState()
		{
			// 게이지는 회복 중 매 프레임 변한다 — 상태 비교 게이트 앞에 둔다.
			refreshBreakGauge();

			bool invincible = _boss.IsInvincible;
			bool breakStun = (_boss.BuffContainer != null && _boss.BuffContainer.Has(EDT.Buff.BUFF_BreakStun));
			if (_stateInitialized == true && invincible == _lastInvincible && breakStun == _lastBreakStun)
			{
				return;
			}

			_stateInitialized = true;
			_lastInvincible = invincible;
			_lastBreakStun = breakStun;
			applyStateVisual(invincible, breakStun);
		}

		private void refreshBreakGauge()
		{
			if (_break == null)
			{
				return;
			}

			float max = _break.Max;
			float ratio = (max > 0f) ? Mathf.Clamp01(_break.Current / max) : 0f;
			if (ratio == _lastBreakRatio)
			{
				return;
			}

			_lastBreakRatio = ratio;
			_breakFill.style.width = Length.Percent(ratio * 100f);
		}

		// 면역 우선 — 둘 다 걸려 있으면 면역 문구·칩을 보여준다(USS 에서 면역 칩 규칙이 뒤에 있어 이긴다).
		// 브레이크 테두리는 무적이 아니라 기절을 본다 — 패턴 전환(무적) 중에 켜지면 안 된다.
		// 클래스(boss--immune / boss--stun)가 게이지 색·테두리와 상태 칩 표시·아이콘을 바꾼다.
		private void applyStateVisual(bool invincible, bool breakStun)
		{
			_banner.EnableInClassList("boss--immune", invincible);
			_banner.EnableInClassList("boss--stun", breakStun);

			if (invincible == true)
			{
				_state.text = getBuffDesc(EDT.Buff.BUFF_Invincible);
			}
			else if (breakStun == true)
			{
				_state.text = getBuffDesc(EDT.Buff.BUFF_BreakStun);
			}
			else
			{
				_state.text = string.Empty;
			}
		}

		private static string getBuffDesc(EDT.Buff id)
		{
			Table_Buff.Row row = Table_Buff.Get(id);
			if (row == null)
			{
				return string.Empty;
			}

			return row.Desc;
		}

		private void onUnitSpawned(UnitSpawnedEvent evt)
		{
			if (evt.UnitType != UnitType.Monster)
			{
				return;
			}

			Table_Monster.Row row = Table_Monster.Get(evt.TableID);
			if (row == null || row.MonsterType != MonsterType.Boss)
			{
				return;
			}

			if (_boss != null)
			{
				_boss.HpChanged -= onBossHpChanged;
			}

			_boss = evt.Unit;
			_break = (_boss != null) ? _boss.GetComponent<MonsterBreak>() : null;
			_breakRow.style.display = (_break != null) ? DisplayStyle.Flex : DisplayStyle.None;

			// 새 보스는 이전 보스와 수치가 같을 수 있다 — 캐시를 비워 첫 갱신을 보장한다.
			_lastHp = -1f;
			_lastMaxHp = -1f;
			_lastPercent = -1;
			_lastBreakRatio = -1f;
			_stateInitialized = false;

			_name.text = row.Name;
			_level.text = (_boss != null) ? $"레벨 {_boss.Level}" : string.Empty;

			if (_boss != null)
			{
				_boss.HpChanged += onBossHpChanged;
				refreshHp();
				refreshState();
			}

			show();
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (_boss == null || evt.InstanceID != _boss.GetID())
			{
				return;
			}

			// 마지막 타격을 반영 — 구독을 끊기 전에 0 HP 를 강제 갱신한다.
			refreshHp();
			detachBoss();

			stopFade();
			_fadeStep = _banner.schedule.Execute(startFade).StartingIn(DeathHoldMs);
		}

		private void startFade()
		{
			_banner.AddToClassList("boss--fading");
			_fadeStep = _banner.schedule.Execute(hide).StartingIn(FadeMs);
		}

		private void show()
		{
			stopFade();
			_banner.RemoveFromClassList("boss--fading");
			_banner.AddToClassList("boss--shown");
			_hud.AddToClassList("hud--boss");
			_showing = true;
		}

		private void hide()
		{
			stopFade();
			_banner.RemoveFromClassList("boss--shown");
			_banner.RemoveFromClassList("boss--fading");
			_hud.RemoveFromClassList("hud--boss");
			_showing = false;
		}

		private void stopFade()
		{
			if (_fadeStep != null)
			{
				_fadeStep.Pause();
				_fadeStep = null;
			}
		}
	}
}
