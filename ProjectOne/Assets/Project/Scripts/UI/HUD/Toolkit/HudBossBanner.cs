using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 메인 HUD 보스 배너.
	// - 보스 몬스터(Table_Monster.MonsterType == Boss) 등장 시 후보로 모으고, 어느 보스를 띄울지는 Tick 에서 정한다
	//   (이웃 구역도 함께 활성돼 필드보스가 여럿 살아 있을 수 있다 — 띄울 조건을 만족하는 보스 하나만 붙잡는다)
	//   · 필드보스: 히어로가 스폰 위치 EnterRadius 안에 들어오면 뜬다. ExitRadius 밖으로 나가면 0.25초 동안 사라진다
	//     (경계에서 깜빡이지 않게 나가는 반경을 넓게 둔다). 보스가 최근 EngageHoldSeconds 안에 피해를 입었으면 반경 밖이어도 유지한다
	//   · 그 밖의 보스(던전 등 보스 전용 스테이지): 등장 즉시 뜬다
	// - 이미 죽은 보스는 추적하지 않으므로 반경 안에 들어와도 뜨지 않는다
	// - 체력은 UnitBase.HpChanged 로 갱신 (게이지 / 현재·최대 수치 / 퍼센트)
	// - 버프 상태(피해 면역·브레이크 기절)는 부착/해제 이벤트가 없어 Tick 에서 폴링한다
	// - 보스 사망 시 USS 전환으로 0.25초 동안 사라진다
	// - 떠 있는 동안 HUD 루트에 hud--boss 를 붙여 같은 자리를 쓰는 미니맵·퀘스트를 숨긴다
	public sealed class HudBossBanner
	{
		private const long FadeMs = 250;
		private const float EnterRadius = 10f;
		private const float ExitRadius = 13f;
		private const float EngageHoldSeconds = 5f;

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

		// 지금 배너에 붙은 보스. 후보 중 띄울 조건을 만족한 하나다.
		private UnitBase _boss;
		// 살아 있는 보스 후보 — 스폰 이벤트로 넣고 사망·회수 시 뺀다.
		private readonly List<UnitBase> _candidates = new List<UnitBase>(4);
		// 반경 판정 기준 — 히어로는 스폰 이벤트로 잡는다.
		private UnitBase _hero;
		// 보스의 브레이크 컴포넌트 — 스폰 시 1회만 잡는다(풀 개체라 수명 내내 유지된다).
		private MonsterBreak _break;
		private IVisualElementScheduledItem _fadeStep;
		private bool _showing;
		private bool _fading;
		// 보스가 마지막으로 피해를 입은 시각 — 반경 밖에서 원거리로 싸우는 동안 배너를 유지한다.
		private float _lastEngageTime = float.NegativeInfinity;

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
			_hero = null;
			_candidates.Clear();
		}

		public void Tick()
		{
			pruneCandidates();

			// 맵 이동·스테이지 정리(풀 반환)·씬 전환(풀 파괴)은 사망이 아니라 UnitDiedEvent 가 나가지 않는다.
			// 배너가 영영 남으므로 여기서 직접 걷는다 — 보스가 이미 없으니 페이드 없이 즉시 감춘다.
			// 참조 자체가 있을 때만 본다 — == null 은 파괴된 보스에도 true 라 걷기 경로를 막는다.
			if (ReferenceEquals(_boss, null) == false && (_boss == null || _boss.isActiveAndEnabled == false))
			{
				detachBoss();
				hide();
			}

			if (_boss != null)
			{
				refreshState();
				if (wantVisible(_boss) == true)
				{
					if (_showing == false || _fading == true)
					{
						show();
					}

					return;
				}
			}

			// 지금 보스를 띄울 수 없으면 조건을 만족하는 다른 보스로 바꾼다.
			UnitBase next = findWantedCandidate();
			if (next != null)
			{
				attachBoss(next);
				show();
				return;
			}

			// 띄울 보스가 없다 — 떠 있으면 0.25초 동안 사라지게 한다.
			if (_showing == true && _fading == false)
			{
				startFade();
			}
		}

		// 회수(풀 반환·파괴)된 후보를 뺀다. 사망은 onUnitDied 가 뺀다.
		private void pruneCandidates()
		{
			for (int i = _candidates.Count - 1; i >= 0; i--)
			{
				UnitBase candidate = _candidates[i];
				if (candidate == null || candidate.isActiveAndEnabled == false || candidate.IsDead == true)
				{
					_candidates.RemoveAt(i);
				}
			}
		}

		private UnitBase findWantedCandidate()
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				UnitBase candidate = _candidates[i];
				if (candidate != _boss && wantVisible(candidate) == true)
				{
					return candidate;
				}
			}

			return null;
		}

		private bool wantVisible(UnitBase boss)
		{
			// 필드보스가 아니면 보스 전용 스테이지다 — 거리와 상관없이 띄운다.
			Monster monster = boss as Monster;
			if (monster == null || monster.FieldBossSpawnId == 0)
			{
				return true;
			}

			bool attached = (boss == _boss);
			if (attached == true && Time.time - _lastEngageTime < EngageHoldSeconds)
			{
				return true;
			}

			if (_hero == null || _hero.isActiveAndEnabled == false)
			{
				return false;
			}

			float radius = (attached == true && _showing == true && _fading == false) ? ExitRadius : EnterRadius;
			Vector2 toOrigin = (Vector2)(_hero.transform.position - monster.SpawnOrigin);
			return toOrigin.sqrMagnitude <= radius * radius;
		}

		// 배너를 이 보스에 붙인다 — 이름·레벨·게이지를 새로 채운다.
		private void attachBoss(UnitBase boss)
		{
			if (_boss != null)
			{
				_boss.HpChanged -= onBossHpChanged;
			}

			_boss = boss;
			_break = _boss.GetComponent<MonsterBreak>();
			_breakRow.style.display = (_break != null) ? DisplayStyle.Flex : DisplayStyle.None;

			// 새 보스는 이전 보스와 수치가 같을 수 있다 — 캐시를 비워 첫 갱신을 보장한다.
			_lastHp = -1f;
			_lastMaxHp = -1f;
			_lastPercent = -1;
			_lastBreakRatio = -1f;
			_stateInitialized = false;
			_lastEngageTime = float.NegativeInfinity;

			Table_Monster.Row row = Table_Monster.Get(_boss.GetTableID());
			_name.text = (row != null) ? row.Name : string.Empty;
			_level.text = $"레벨 {_boss.Level}";

			_boss.HpChanged += onBossHpChanged;
			refreshHp();
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
			// 줄었을 때만 교전으로 본다 — 리쉬 리셋·회복으로 늘어난 것은 싸움이 아니다.
			if (_lastHp >= 0f && _boss != null && _boss.Vitals.Hp < _lastHp)
			{
				_lastEngageTime = Time.time;
			}

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
			if (evt.UnitType == UnitType.Hero)
			{
				_hero = evt.Unit;
				return;
			}

			if (evt.UnitType != UnitType.Monster)
			{
				return;
			}

			Table_Monster.Row row = Table_Monster.Get(evt.TableID);
			if (row == null || row.MonsterType != MonsterType.Boss || evt.Unit == null)
			{
				return;
			}

			// 띄울지는 다음 Tick 이 정한다 — 필드보스 표시(SetFieldBoss)가 스폰 이벤트 뒤에 붙기 때문이다.
			if (_candidates.Contains(evt.Unit) == false)
			{
				_candidates.Add(evt.Unit);
			}
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			for (int i = _candidates.Count - 1; i >= 0; i--)
			{
				if (_candidates[i] != null && _candidates[i].GetID() == evt.InstanceID)
				{
					_candidates.RemoveAt(i);
				}
			}

			if (_boss == null || evt.InstanceID != _boss.GetID())
			{
				return;
			}

			// 마지막 타격을 반영 — 구독을 끊기 전에 0 HP 를 강제 갱신한다.
			refreshHp();
			detachBoss();

			if (_showing == true && _fading == false)
			{
				startFade();
			}
		}

		private void startFade()
		{
			stopFade();
			_fading = true;
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
			_fading = false;
		}

		private void hide()
		{
			stopFade();
			_banner.RemoveFromClassList("boss--shown");
			_banner.RemoveFromClassList("boss--fading");
			_hud.RemoveFromClassList("hud--boss");
			_showing = false;
			_fading = false;
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
