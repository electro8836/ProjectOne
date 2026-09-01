using UnityEngine;
using DamageNumbersPro;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Combat
{
	// 피격 수치 팝업 관리자 — DamageTakenEvent 를 구독해 DamageNumbersPro 프리팹을 띄운다.
	// 풀링·이동·페이드·색상은 전부 라이브러리와 프리팹 설정이 담당한다.
	// 코드로 SetScale/SetColor 를 건드리지 않는다 — 풀에서 재사용된 인스턴스에 이전 값이 남기 때문.
	//
	// 부트 씬에 프리팹으로 배치되는 것이 전제다 — 인스펙터 프리팹 참조를 들고 있어서
	// MonoSingleton 의 빈 GameObject 자동생성 폴백을 타면 참조가 전부 null 인 껍데기가 된다.
	// (공개 API 가 없어 Instance 를 부르는 곳이 없으므로 실제로 그 경로를 타지는 않는다)
	public sealed class DamageTextManager : MonoSingleton<DamageTextManager>
	{
		[Header("몬스터 피격")]
		[SerializeField] private DamageNumber _monsterHitPrefab;
		[SerializeField] private DamageNumber _monsterCritPrefab;

		[Header("히어로/소환물 피격")]
		[SerializeField] private DamageNumber _heroHitPrefab;

		[Header("회복")]
		[SerializeField] private DamageNumber _healPrefab;

		// 무효화 표시 — 문구(MISS/BLOCK)는 프리팹의 텍스트가 갖는다. 코드는 위치만 넘긴다.
		[Header("무효화")]
		[SerializeField] private DamageNumber _missPrefab;
		[SerializeField] private DamageNumber _blockPrefab;

		// 브레이크 발동 표시 — 문구(BREAK)는 프리팹의 텍스트가 갖는다.
		[Header("브레이크")]
		[SerializeField] private DamageNumber _breakPrefab;

		// HitCenter 로부터 위로 띄울 높이
		[SerializeField] private float _yOffset = 0.5f;

		protected override void Awake()
		{
			base.Awake();

			// 중복 인스턴스라 파괴 예정이면(_instance != this) 예열 생략
			if (Instance != this)
			{
				return;
			}

			// 팝업 풀의 부모를 이 매니저로 지정 — 지정하지 않으면 DNP 가
			// "Damage Number Pool" 오브젝트를 따로 만들어 팝업을 거기에 쌓는다.
			// 예열(PrewarmPool)이 내부에서 Spawn 을 돌리므로 반드시 그 전에 대입해야 한다.
			// 매니저는 영속(DontDestroyOnLoad)이고 Transform 이 identity 라 풀 부모 조건을 만족한다.
			DamageNumber.poolParent = transform;

			prewarm(_monsterHitPrefab);
			prewarm(_monsterCritPrefab);
			prewarm(_heroHitPrefab);
			prewarm(_healPrefab);
			prewarm(_missPrefab);
			prewarm(_blockPrefab);
			prewarm(_breakPrefab);
		}

		private void OnEnable()
		{
			EventManager.Instance.Subscribe<DamageTakenEvent>(onDamageTaken);
			EventManager.Instance.Subscribe<HealAppliedEvent>(onHealApplied);
			EventManager.Instance.Subscribe<DamageAvoidedEvent>(onDamageAvoided);
			EventManager.Instance.Subscribe<MonsterBrokenEvent>(onMonsterBroken);
		}

		private void OnDisable()
		{
			EventManager.Instance.Unsubscribe<DamageTakenEvent>(onDamageTaken);
			EventManager.Instance.Unsubscribe<HealAppliedEvent>(onHealApplied);
			EventManager.Instance.Unsubscribe<DamageAvoidedEvent>(onDamageAvoided);
			EventManager.Instance.Unsubscribe<MonsterBrokenEvent>(onMonsterBroken);
		}

		// 프리팹당 1회 호출 — enablePooling 이 꺼져 있으면 라이브러리가 알아서 무시한다.
		private void prewarm(DamageNumber prefab)
		{
			if (prefab == null)
			{
				return;
			}

			prefab.PrewarmPool();
		}

		private void onDamageTaken(DamageTakenEvent e)
		{
			if (e.Target == null || e.Damage <= 0)
			{
				return;
			}

			DamageNumber prefab;
			if (e.Target.GetUnitType() == UnitType.Monster)
			{
				if (e.IsCritical == true)
				{
					prefab = _monsterCritPrefab;
				}
				else
				{
					prefab = _monsterHitPrefab;
				}
			}
			else
			{
				prefab = _heroHitPrefab;
			}

			// 인스펙터 미연결 방어
			if (prefab == null)
			{
				return;
			}

			prefab.Spawn(spawnPos(e.Target), e.Damage);
		}

		private void onHealApplied(HealAppliedEvent e)
		{
			if (e.Target == null || e.Amount <= 0)
			{
				return;
			}

			// 인스펙터 미연결 방어
			if (_healPrefab == null)
			{
				return;
			}

			_healPrefab.Spawn(spawnPos(e.Target), e.Amount);
		}

		private void onDamageAvoided(DamageAvoidedEvent e)
		{
			if (e.Target == null)
			{
				return;
			}

			DamageNumber prefab;
			if (e.IsBlocked == true)
			{
				prefab = _blockPrefab;
			}
			else
			{
				prefab = _missPrefab;
			}

			// 인스펙터 미연결 방어
			if (prefab == null)
			{
				return;
			}

			// 숫자 없이 띄운다 — 문구는 프리팹이 갖는다.
			prefab.Spawn(spawnPos(e.Target));
		}

		private void onMonsterBroken(MonsterBrokenEvent e)
		{
			if (e.Target == null)
			{
				return;
			}

			// 인스펙터 미연결 방어
			if (_breakPrefab == null)
			{
				return;
			}

			// 숫자 없이 띄운다 — 문구는 프리팹이 갖는다.
			_breakPrefab.Spawn(spawnPos(e.Target));
		}

		// 대상이 죽거나 풀로 반환되기 전에 위치를 즉시 읽는다 (이벤트에 좌표가 실려 있지 않음).
		private Vector3 spawnPos(UnitBase target)
		{
			Vector2 center = target.HitCenter;
			return new Vector3(center.x, center.y + _yOffset, target.transform.position.z);
		}
	}
}
