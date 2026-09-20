using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.Mastery;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 내 히어로 정보 패널 — 체력 / 레벨 / 전투력.
	//
	// BossUI 와 같은 결의 자율 컴포넌트다(MVP 를 쓰지 않는다). MainHud 는 화면(UIScreen)이라
	// Presenter 를 두지만, 이쪽은 HUD 위젯이라 자기 이벤트를 직접 구독한다.
	//
	// **프리팹 루트에 Canvas 가 붙어 있다(리빌드 격리).** 체력은 피격마다 변하는데 같은 캔버스에
	// 있으면 그 배치 리빌드가 MainHUD 전체로 번진다. ResourceBar 와 같은 처리다 —
	// GraphicRaycaster 는 붙이지 않는다(HUD 캔버스의 레이캐스터를 공유한다).
	//
	// Canvas 는 배칭 경계일 뿐 레이아웃 경계가 아니므로, 실제 비용을 막는 것은 아래의
	// _lastXxx 캐시 게이트다 — 같은 문자열을 다시 대입해도 TMP 는 매번 dirty 가 된다.
	public class HeroInfo : MonoBehaviour
	{
		[Header("체력")]
		[SerializeField] private Slider _hpSlider;
		[SerializeField] private TMP_Text _hpValueText;

		[Header("레벨 / 전투력")]
		[SerializeField] private TMP_Text _levelText;
		[SerializeField] private TMP_Text _battlePowerText;

		private Action<UnitSpawnedEvent> _onUnitSpawned;
		private Action<CharacterChangeEvent> _onCharacterChanged;
		private Action<MasteryChangeEvent> _onMasteryChanged;
		private Action<PresetChangeEvent> _onPresetChanged;

		// 추적 중인 히어로. 씬마다 새로 스폰되고 부활 때도 다시 통지되므로 갈아끼운다.
		private UnitBase _hero;

		// 마지막으로 그린 값 — 변화가 있을 때만 대입한다.
		private float _lastHp = -1f;
		private float _lastMaxHp = -1f;
		private int _lastLevel = -1;
		private int _lastMasteryLevel = -1;

		// 전투력은 전용 이벤트가 없어 스탯 버전으로 감지한다 (게임플레이 쪽 UnitBase.tickHpNotify 와 같은 방식).
		// 영향 이벤트(EquipmentChange/MasteryChange/PetChange/…)를 구독하면 Aspect 재적용과 발행 순서가
		// 보장되지 않아 한 틱 낡은 값을 그릴 수 있다. Version 은 재적용이 끝난 뒤 반드시 올라간다.
		private int _lastStatVersion = -1;
		private int _lastBattlePower = -1;

		private void Awake()
		{
			_onUnitSpawned = onUnitSpawned;
			_onCharacterChanged = onCharacterChanged;
			_onMasteryChanged = onMasteryChanged;
			_onPresetChanged = onPresetChanged;

			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(_onCharacterChanged);
			EventManager.Instance.Subscribe<MasteryChangeEvent>(_onMasteryChanged);
			EventManager.Instance.Subscribe<PresetChangeEvent>(_onPresetChanged);

			refreshLevel();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(_onCharacterChanged);
			EventManager.Instance.Unsubscribe<MasteryChangeEvent>(_onMasteryChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(_onPresetChanged);

			if (_hero != null)
			{
				_hero.HpChanged -= onHpChanged;
			}

			_hero = null;
		}

		// 전투력만 폴링한다 — 체력과 레벨은 이벤트로 들어온다.
		private void Update()
		{
			// 씬 전환으로 파괴된 히어로를 걸러낸다. 다음 UnitSpawnedEvent 가 새 참조를 준다.
			if (_hero == null)
			{
				return;
			}

			refreshBattlePower();
		}

		// ── 체력 ──────────────────────────────────────────────────────

		// 히어로의 체력/최대체력이 바뀐 프레임에 1회 불린다 (UnitBase 가 프레임 경계에서 모아 발행한다).
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

			if (_hpSlider != null)
			{
				_hpSlider.value = (maxHp > 0f) ? Mathf.Clamp01(hp / maxHp) : 0f;
			}

			if (_hpValueText != null)
			{
				_hpValueText.text = Mathf.CeilToInt(hp).ToString("N0") + " / " + Mathf.CeilToInt(maxHp).ToString("N0");
			}
		}

		// ── 레벨 ──────────────────────────────────────────────────────

		// 히어로레벨(마스터리레벨) 형식. 무기를 착용하지 않았으면 마스터리가 없어 괄호를 생략한다.
		private void refreshLevel()
		{
			if (_levelText == null)
			{
				return;
			}

			int level = Account.Instance.Loadout.Level;

			MasteryProgress progress = Account.Instance.Mastery.CurrentProgress;
			int masteryLevel = (progress != null) ? progress.Level : 0;

			if (level == _lastLevel && masteryLevel == _lastMasteryLevel)
			{
				return;
			}

			_lastLevel = level;
			_lastMasteryLevel = masteryLevel;

			if (masteryLevel > 0)
			{
				_levelText.text = level.ToString() + "(" + masteryLevel.ToString() + ")";
			}
			else
			{
				_levelText.text = level.ToString();
			}
		}

		// ── 전투력 ────────────────────────────────────────────────────

		private void refreshBattlePower()
		{
			if (_battlePowerText == null || _hero.Stats == null)
			{
				return;
			}

			int version = _hero.Stats.Version;
			if (version == _lastStatVersion)
			{
				return;
			}

			_lastStatVersion = version;

			// 버프·디버프는 계산에서 배제되므로, 버프가 붙고 떨어져도 결과는 그대로다 —
			// 아래 캐시 게이트에서 걸러져 텍스트를 건드리지 않는다.
			int power = BattlePowerCalculator.Calculate(_hero.Stats, _hero.SkillContainer);
			if (power == _lastBattlePower)
			{
				return;
			}

			_lastBattlePower = power;
			_battlePowerText.text = power.ToString("N0");
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 부활·씬 전환으로 여러 번 도착한다. 구독을 갈아끼우고 캐시를 전부 무효화해야
		// 같은 값이 들어와도 다시 그린다.
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

			// 히어로 스폰은 Aspect 적용이 끝난 뒤 통지되므로 Loadout 이 확실히 채워진 시점이다.
			// Awake 가 데이터보다 앞서는 경우의 보험이다.
			refreshLevel();
		}

		// 레벨업·경험치 적립이 함께 타는 통지다. 마스터리 레벨업은 전용 이벤트가 없고
		// 경험치가 항상 Account.AddExp 를 지나므로 이쪽으로 같이 들어온다.
		private void onCharacterChanged(CharacterChangeEvent e)
		{
			refreshLevel();
		}

		// 스킬 트리 투자·환불·초기화. 마스터리 레벨은 경험치 파생이라 이 신호로는 바뀌지 않지만,
		// "마스터리 상태가 변했다"는 의미의 신호라 함께 본다 — 값이 같으면 게이트에서 걸러진다.
		private void onMasteryChanged(MasteryChangeEvent e)
		{
			refreshLevel();
		}

		// 장비 슬롯 변경 — **무기 교체가 마스터리 대상을 통째로 갈아치우는 유일한 경로다**
		// (Loadout.setSlotValue). 이걸 놓치면 무기를 장착해도 괄호 안 레벨이 영영 0으로 남는다.
		// 무기 외 슬롯도 이 신호를 쏘지만 값이 같으면 게이트에서 걸러진다.
		private void onPresetChanged(PresetChangeEvent e)
		{
			refreshLevel();
		}
	}
}
