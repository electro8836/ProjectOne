using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 보스 정보 배너.
	// - 보스 몬스터(Table_Monster.MonsterType == Boss) 등장 시 표시
	// - 체력은 UnitBase.HpChanged 이벤트로 갱신 (슬라이더 퍼센트 / 현재·최대 수치 / 퍼센트 텍스트)
	// - 버프 상태(피해 면역·브레이크 기절)는 부착/해제 이벤트가 없어 Update 에서 폴링한다
	// - 보스 사망 시 _deathHoldDuration 유지 후 _fadeDuration 동안 페이드아웃
	public class BossUI : MonoBehaviour
	{
		[Header("참조")]
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private TMP_Text _nameText;
		[SerializeField] private Slider _hpSlider;
		[SerializeField] private Image _hpFillImage;
		[SerializeField] private Image _hpBorderImage;
		[SerializeField] private TMP_Text _hpValueText;
		[SerializeField] private TMP_Text _hpPercentText;
		[SerializeField] private TMP_Text _bossStateText;
		[SerializeField] private Slider _breakSlider;
		[SerializeField] private Image _breakBorderImage;

		[Header("연출")]
		[SerializeField] private float _deathHoldDuration = 3f;
		[SerializeField] private float _fadeDuration = 1f;

		// 피해 면역 상태 / 일반 상태 색상. 기획 고정값이므로 인스펙터로 노출하지 않는다.
		private static readonly Color ImmuneFillColor = new Color32(0x40, 0xE7, 0xFF, 0xFF);
		private static readonly Color NormalFillColor = new Color32(0xFF, 0x45, 0x3D, 0xFF);
		private static readonly Color ImmuneBorderColor = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
		private static readonly Color NormalBorderColor = new Color32(0x1E, 0x1C, 0x36, 0xFF);

		private Action<UnitSpawnedEvent> _onUnitSpawned;
		private Action<UnitDiedEvent> _onUnitDied;
		private UnitBase _boss;

		// 보스의 브레이크 컴포넌트 — 매 프레임 GetComponent 를 부르지 않으려고 스폰 시 1회만 잡는다.
		// 풀 개체라 컴포넌트가 개체 수명 내내 유지된다 (MonsterPool 이 등급을 보고 1회 부착).
		private MonsterBreak _break;
		private Coroutine _fadeRoutine;

		// 마지막으로 표시한 체력 — 값이 그대로면 텍스트를 다시 만들지 않는다.
		// HpChanged 는 한 프레임에 여러 번 올 수 있고, 문자열 연결은 그때마다 할당이 생긴다.
		private float _lastHp = -1f;
		private float _lastMaxHp = -1f;
		private int _lastPercent = -1;

		// 마지막으로 표시한 버프 상태 — 폴링이므로 값이 바뀐 프레임에만 색상/문구를 갱신한다.
		private bool _lastInvincible;
		private bool _lastBreakStun;
		private bool _stateInitialized;

		// 마지막으로 표시한 브레이크 비율 — 회복 중에는 매 프레임 변하므로 값이 바뀐 프레임에만 대입한다.
		private float _lastBreakRatio = -1f;

		// 배너가 화면에 떠 있는가. 웨이브 배너가 자리를 비켜줄지 판단하는 근거다(보스 우선).
		public bool IsShowing
		{
			get { return this.gameObject.activeSelf; }
		}

		private void Awake()
		{
			_onUnitSpawned = onUnitSpawned;
			_onUnitDied = onUnitDied;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Subscribe<UnitDiedEvent>(_onUnitDied);

			// 프리팹은 활성 상태로 저장돼 있어야 한다 — 비활성으로 저장하면 Awake 가 돌지 않아
			// 위 구독이 없고 보스가 영영 뜨지 않는다. 구독은 오브젝트가 꺼져도 유지된다.
			this.gameObject.SetActive(false);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(_onUnitDied);

			if (_boss != null)
			{
				_boss.HpChanged -= onBossHpChanged;
			}
		}

		private void Update()
		{
			if (_boss == null)
			{
				return;
			}

			// 맵 이동·스테이지 정리(MonsterSpawnManager.ClearAlive)는 사망이 아니라 풀 반환이라
			// UnitDiedEvent 가 나가지 않는다. 파괴가 아니라 SetActive(false) 뿐이라 == null 에도 안 걸린다.
			// 그 경우 배너가 영영 남으므로 여기서 직접 걷는다 — 보스가 이미 없으니 페이드 없이 즉시 감춘다.
			if (_boss.isActiveAndEnabled == false)
			{
				detachBoss();
				stopFade();
				this.gameObject.SetActive(false);
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

			// 페이드아웃/재사용 동안 면역·기절 표시가 남지 않도록 일반 상태로 되돌린다.
			_stateInitialized = false;
			applyStateVisual(false, false);
		}

		// 보스의 체력/최대체력이 바뀐 프레임에 1회 불린다 (UnitBase.HpChanged).
		private void onBossHpChanged(UnitBase unit)
		{
			refreshHp();
		}

		// 현재 보스 HP를 슬라이더(퍼센트)/수치 텍스트/퍼센트 텍스트에 반영
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
			if (_hpSlider != null)
			{
				_hpSlider.value = ratio;
			}

			if (_hpValueText != null)
			{
				_hpValueText.text = Mathf.CeilToInt(hp).ToString() + " / " + Mathf.CeilToInt(max).ToString();
			}

			if (_hpPercentText == null)
			{
				return;
			}

			int percent = Mathf.RoundToInt(ratio * 100f);
			if (percent == _lastPercent)
			{
				return;
			}

			_lastPercent = percent;
			_hpPercentText.text = percent.ToString() + "%";
		}

		// 보스의 버프 상태를 색상/문구에 반영. 상태가 바뀐 프레임에만 실제로 대입한다.
		private void refreshState()
		{
			// 게이지는 회복 중 매 프레임 변한다 — 아래 상태 비교 게이트에 걸리면 안 되므로 앞에 둔다.
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

		// 브레이크 게이지를 슬라이더(0~1)에 반영한다. 브레이크가 없는 보스면 손대지 않는다.
		private void refreshBreakGauge()
		{
			if (_break == null || _breakSlider == null)
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
			_breakSlider.value = ratio;
		}

		// 면역 우선 — 둘 다 걸려 있으면 면역 문구를 보여준다.
		private void applyStateVisual(bool invincible, bool breakStun)
		{
			if (_hpFillImage != null)
			{
				_hpFillImage.color = (invincible == true) ? ImmuneFillColor : NormalFillColor;
			}

			if (_hpBorderImage != null)
			{
				_hpBorderImage.color = (invincible == true) ? ImmuneBorderColor : NormalBorderColor;
			}

			// 브레이크 테두리는 무적이 아니라 기절을 본다 — 패턴 전환(무적) 중에 켜지면 안 된다.
			if (_breakBorderImage != null)
			{
				_breakBorderImage.color = (breakStun == true) ? ImmuneBorderColor : NormalBorderColor;
			}

			if (_bossStateText == null)
			{
				return;
			}

			if (invincible == true)
			{
				_bossStateText.text = getBuffDesc(EDT.Buff.BUFF_Invincible);
			}
			else if (breakStun == true)
			{
				_bossStateText.text = getBuffDesc(EDT.Buff.BUFF_BreakStun);
			}
			else
			{
				_bossStateText.text = string.Empty;
			}
		}

		private string getBuffDesc(EDT.Buff id)
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

			// 새 보스는 이전 보스와 수치가 같을 수 있다 — 캐시를 비워 첫 갱신을 보장한다.
			_lastHp = -1f;
			_lastMaxHp = -1f;
			_lastPercent = -1;
			_lastBreakRatio = -1f;
			_stateInitialized = false;

			if (_nameText != null)
			{
				_nameText.text = row.Name;
			}

			if (_boss != null)
			{
				_boss.HpChanged += onBossHpChanged;
				refreshHp();
				refreshState();
			}

			stopFade();

			// 직전 사망 페이드가 알파를 0 으로 남겨 두므로 되돌린다.
			_canvasGroup.alpha = 1f;
			this.gameObject.SetActive(true);
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (_boss == null || evt.InstanceID != _boss.GetID())
			{
				return;
			}

			// 마지막 타격(킬 데미지)을 반영 — 구독을 끊기 전에 0 HP를 강제 갱신
			refreshHp();

			detachBoss();

			stopFade();
			_fadeRoutine = StartCoroutine(holdThenFade());
		}

		private IEnumerator holdThenFade()
		{
			yield return new WaitForSeconds(_deathHoldDuration);

			float elapsed = 0f;
			while (elapsed < _fadeDuration)
			{
				elapsed += Time.deltaTime;
				_canvasGroup.alpha = Mathf.Clamp01(1f - elapsed / _fadeDuration);
				yield return null;
			}

			_fadeRoutine = null;
			this.gameObject.SetActive(false);
		}

		private void stopFade()
		{
			if (_fadeRoutine != null)
			{
				StopCoroutine(_fadeRoutine);
				_fadeRoutine = null;
			}
		}
	}
}
