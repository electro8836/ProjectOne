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
	// - 표시 중 매 프레임 체력을 슬라이더(퍼센트)와 텍스트(현재/최대)로 갱신
	// - 보스 사망 시 _deathHoldDuration 유지 후 _fadeDuration 동안 페이드아웃
	public class BossUI : MonoBehaviour
	{
		[Header("참조")]
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private Slider _hpSlider;
		[SerializeField] private TMP_Text _hpText;

		[Header("연출")]
		[SerializeField] private float _deathHoldDuration = 3f;
		[SerializeField] private float _fadeDuration = 1f;

		private Action<UnitSpawnedEvent> _onUnitSpawned;
		private Action<UnitDiedEvent> _onUnitDied;
		private UnitBase _boss;
		private Coroutine _fadeRoutine;

		// 마지막으로 표시한 체력 — 값이 그대로면 텍스트를 다시 만들지 않는다.
		// 체력은 이벤트가 없어 매 프레임 폴링하는데, 문자열 연결은 그때마다 할당이 생긴다.
		private float _lastHp = -1f;
		private float _lastMaxHp = -1f;

		private void Awake()
		{
			_onUnitSpawned = onUnitSpawned;
			_onUnitDied = onUnitDied;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Subscribe<UnitDiedEvent>(_onUnitDied);

			_canvasGroup.alpha = 0f;
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

		// 보스의 체력/최대체력이 바뀐 프레임에 1회 불린다 (UnitBase.HpChanged).
		private void onBossHpChanged(UnitBase unit)
		{
			refreshHp();
		}

		// 현재 보스 HP를 슬라이더(퍼센트)/텍스트(현재/최대)에 반영
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

			_hpSlider.value = (max > 0f) ? (hp / max) : 0f;
			_hpText.text = Mathf.CeilToInt(hp) + "/" + Mathf.CeilToInt(max);
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

			// 새 보스는 이전 보스와 수치가 같을 수 있다 — 캐시를 비워 첫 갱신을 보장한다.
			_lastHp = -1f;
			_lastMaxHp = -1f;

			if (_boss != null)
			{
				_boss.HpChanged += onBossHpChanged;
				refreshHp();
			}

			stopFade();
			_canvasGroup.alpha = 1f;
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (_boss == null || evt.InstanceID != _boss.GetID())
			{
				return;
			}

			// 마지막 타격(킬 데미지)을 반영 — 구독을 끊기 전에 0 HP를 강제 갱신
			refreshHp();

			_boss.HpChanged -= onBossHpChanged;
			_boss = null;
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

			_canvasGroup.alpha = 0f;
			_fadeRoutine = null;
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
