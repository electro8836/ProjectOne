using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;
using ProjectOne.Mastery;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 메인 HUD 왼쪽 위 히어로 정보 — 체력 / 숙련도 경험치 / 레벨(장착 무기의 숙련도) / 전투력.
	//
	// 자기 이벤트를 직접 구독하는 HUD 위젯이다. 체력·레벨·경험치는 이벤트로, 전투력은 스탯 버전 폴링(Tick)으로 갱신한다.
	// 값이 바뀌었을 때만 요소에 쓴다 — 체력은 피격마다 변한다.
	public sealed class HudHeroInfo
	{
		private readonly VisualElement _hpFill;
		private readonly Label _hpText;
		private readonly VisualElement _expFill;
		private readonly Label _levelText;
		private readonly Label _powerText;

		// 추적 중인 히어로. 씬마다 새로 스폰되고 부활 때도 다시 통지되므로 갈아끼운다.
		private UnitBase _hero;

		private float _lastHp = -1f;
		private float _lastMaxHp = -1f;
		private int _lastMasteryLevel = -1;
		private float _lastExpRatio = -1f;

		// 전투력은 전용 이벤트가 없어 스탯 버전으로 감지한다 — Version 은 Aspect 재적용이 끝난 뒤 반드시 올라간다.
		private int _lastStatVersion = -1;
		private int _lastBattlePower = -1;

		public HudHeroInfo(VisualElement root)
		{
			_hpFill = root.Q("hp-fill");
			_hpText = root.Q<Label>("hp-text");
			_expFill = root.Q("exp-fill");
			_levelText = root.Q<Label>("level-text");
			_powerText = root.Q<Label>("power-text");

			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);
			EventManager.Instance.Subscribe<MasteryExpChangeEvent>(onMasteryExpChanged);
			EventManager.Instance.Subscribe<MasteryChangeEvent>(onMasteryChanged);
			EventManager.Instance.Subscribe<PresetChangeEvent>(onPresetChanged);

			refreshLevel();
			refreshExp();
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);
			EventManager.Instance.Unsubscribe<MasteryExpChangeEvent>(onMasteryExpChanged);
			EventManager.Instance.Unsubscribe<MasteryChangeEvent>(onMasteryChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(onPresetChanged);

			if (_hero != null)
			{
				_hero.HpChanged -= onHpChanged;
			}

			_hero = null;
		}

		// 매 프레임 — 전투력만 폴링한다.
		public void Tick()
		{
			// 씬 전환으로 파괴된 히어로를 걸러낸다. 다음 UnitSpawnedEvent 가 새 참조를 준다.
			if (_hero == null)
			{
				return;
			}

			refreshBattlePower();
		}

		// ── 체력 ──────────────────────────────────────────────────────

		private void onHpChanged(UnitBase unit)
		{
			refreshHp();
		}

		private void refreshHp()
		{
			if (_hero == null || _hero.Vitals == null || _hero.Stats == null)
			{
				return;
			}

			float hp = _hero.Vitals.Hp;
			// 최대 체력은 Vitals 가 아니라 스탯 층위에 있다.
			float maxHp = _hero.Stats.GetStat(Stat.Stat_MaxHp);
			if (hp == _lastHp && maxHp == _lastMaxHp)
			{
				return;
			}

			_lastHp = hp;
			_lastMaxHp = maxHp;

			float ratio = (maxHp > 0f) ? Mathf.Clamp01(hp / maxHp) : 0f;
			_hpFill.style.width = Length.Percent(ratio * 100f);
			_hpText.text = Mathf.CeilToInt(hp).ToString("N0") + " / " + Mathf.CeilToInt(maxHp).ToString("N0");
		}

		// ── 레벨 · 경험치 ─────────────────────────────────────────────

		// 대표 레벨 = 장착 무기의 숙련도 레벨. 무기를 착용하지 않았으면 비운다.
		private void refreshLevel()
		{
			int masteryLevel = Account.Instance.Mastery.RepresentativeLevel;
			if (masteryLevel == _lastMasteryLevel)
			{
				return;
			}

			_lastMasteryLevel = masteryLevel;
			_levelText.text = (masteryLevel > 0) ? "Lv." + masteryLevel.ToString() : string.Empty;
		}

		// 레벨이 같아도 경험치는 계속 오르므로 레벨과 따로 비교한다.
		private void refreshExp()
		{
			float ratio = 0f;
			MasteryProgress progress = Account.Instance.Mastery.CurrentProgress;
			if (progress != null)
			{
				int level = progress.Level;
				ratio = bandRatio(progress.TotalExp,
					MasteryCatalog.GetMasteryTotalExp(level),
					MasteryCatalog.GetMasteryTotalExp(level + 1));
			}

			if (Mathf.Approximately(_lastExpRatio, ratio) == true)
			{
				return;
			}

			_lastExpRatio = ratio;
			_expFill.style.width = Length.Percent(ratio * 100f);
		}

		// 현재 레벨 구간의 진행도. 테이블 값이 누적치라 구간을 떼어낸다. 다음 레벨이 없으면(만렙) 꽉 채운다.
		private static float bandRatio(int exp, int currentTotal, int nextTotal)
		{
			if (nextTotal <= currentTotal)
			{
				return 1f;
			}

			return Mathf.Clamp01((float)(exp - currentTotal) / (nextTotal - currentTotal));
		}

		// ── 전투력 ────────────────────────────────────────────────────

		private void refreshBattlePower()
		{
			if (_hero.Stats == null)
			{
				return;
			}

			int version = _hero.Stats.Version;
			if (version == _lastStatVersion)
			{
				return;
			}

			_lastStatVersion = version;

			int power = BattlePowerCalculator.Calculate(_hero.Stats, _hero.SkillContainer);
			if (power == _lastBattlePower)
			{
				return;
			}

			_lastBattlePower = power;
			_powerText.text = power.ToString("N0");
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 부활·씬 전환으로 여러 번 도착한다. 구독을 갈아끼우고 캐시를 무효화해야 같은 값도 다시 그린다.
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
			_lastStatVersion = -1;
			_lastBattlePower = -1;

			if (_hero == null)
			{
				return;
			}

			_hero.HpChanged += onHpChanged;
			refreshHp();
			refreshBattlePower();
			refreshLevel();
			refreshExp();
		}

		private void onMasteryExpChanged(MasteryExpChangeEvent e)
		{
			refreshLevel();
			refreshExp();
		}

		private void onMasteryChanged(MasteryChangeEvent e)
		{
			refreshLevel();
			refreshExp();
		}

		// 무기 교체가 마스터리 대상을 통째로 갈아치우는 유일한 경로다.
		private void onPresetChanged(PresetChangeEvent e)
		{
			refreshLevel();
			refreshExp();
		}
	}
}
