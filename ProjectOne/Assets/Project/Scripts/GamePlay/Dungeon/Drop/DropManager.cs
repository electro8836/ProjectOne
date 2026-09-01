using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Resources;
using ProjectOne.Reward;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 월드 오브젝트 매니저(전투씬 수명). 타입별 풀 생성/정리와 스폰을 담당한다.
	// 필드·던전 양쪽에서 쓴다 — Persistent 가 false 라 마을로 따라가지 않는다.
	//
	// 세 종류를 스폰한다.
	//   HealOrb  — 몬스터 사망 위치에 확률로
	//   Item     — 처치 보상(RewardGranter.Roll 결과)을 실어 사망 위치 주변에 산포
	//   BuffRune — 히어로 주변에 랜덤 간격으로
	//
	// **보상 지급은 여기가 아니라 DropObject 가 한다.** 여기서는 굴려 둔 결과를 실어 보내기만 하고,
	// 히어로가 획득 범위에 들어온 순간 DropObject 가 인벤에 넣는다.
	public sealed class DropManager : MonoSingleton<DropManager>
	{
		// 드랍 산포 반경 (사망 위치 주변)
		private const float ScatterRadius = 0.5f;

		// 프리팹 주소 — 씬 직렬화로 숨는 것을 막기 위해 코드 상수로 고정한다.
		private const string HealOrbAddress = "Prefab_HealOrb";
		private const string BuffRuneAddress = "Prefab_BuffRune";
		private const string DropItemAddress = "Prefab_DropItem";
		private const string BossGimmickAddress = "Prefab_BossGimmickCore";

		// 몬스터 처치 시 회복 오브가 등장할 확률
		private const float HealOrbChance = 0.15f;

		// [임시] 보상 테이블(Monster/MonsterSpawn 의 RewardGroupID)이 비어 있어 드랍 연출을 볼 수 없다.
		// 처치마다 페이로드 없는 드랍을 떨궈 연출만 확인한다 — 주워도 인벤토리는 변하지 않는다.
		// 정식 보상 데이터가 들어오면 이 상수와 onUnitDied 의 사용 블록을 함께 지운다.
		private const bool TestDropOnKill = true;
		private const int TestDropCountMin = 2;
		private const int TestDropCountMax = 3;

		// 버프룬 생성 간격 범위(초)
		private const float RuneIntervalMin = 5f;
		private const float RuneIntervalMax = 10f;

		// 버프룬이 히어로로부터 떨어져 생성될 거리 범위(유닛)
		private const float RuneSpawnDistMin = 2f;
		private const float RuneSpawnDistMax = 4f;

		[Header("풀 용량")]
		[SerializeField] private int _defaultPoolCapacity = 16;

		protected override bool Persistent => false;

		// 타입별 풀 / 풀 프리팹 주소(해제용)
		private readonly Dictionary<DropObjectType, DropObjectPool> _pools = new Dictionary<DropObjectType, DropObjectPool>();
		private readonly Dictionary<DropObjectType, string> _poolPaths = new Dictionary<DropObjectType, string>();

		// 다음 버프룬 생성까지 남은 시간. 풀이 준비되기 전에는 카운트다운하지 않는다.
		private float _runeTimer;

		// 보상 1건을 담아 넘기기 위한 재사용 버퍼 — 스폰은 메인 스레드 단일 경로다.
		private readonly List<GrantedReward> _single = new List<GrantedReward>(1);

		protected override void Awake()
		{
			base.Awake();
			EventManager.Instance.Subscribe<UnitDiedEvent>(onUnitDied);
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(onUnitDied);
			base.OnDestroy();
		}

		// 전투씬 진입 시 1회 호출 — 이전 풀을 정리하고 세 종류를 미리 만들어 둔다.
		public async UniTask PrepareAsync(CancellationToken ct)
		{
			clearPools();

			await createPoolAsync(DropObjectType.HealOrb, HealOrbAddress, ct);
			await createPoolAsync(DropObjectType.BuffRune, BuffRuneAddress, ct);
			await createPoolAsync(DropObjectType.Item, DropItemAddress, ct);
			await createPoolAsync(DropObjectType.BossGimmick, BossGimmickAddress, ct);

			resetRuneTimer();
		}

		// 던전 종료 시 호출 — 풀 일괄 정리.
		public void Clear()
		{
			clearPools();
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (evt.UnitType != UnitType.Monster)
			{
				return;
			}

			// 회복 오브는 산포하지 않는다 — 쓰러진 자리에 그대로 뜬다.
			if (Random.value < HealOrbChance)
			{
				spawnDrop(DropObjectType.HealOrb, evt.Position);
			}

			// [임시] 연출 확인용 — SetPayload 를 부르지 않으므로 주워도 지급이 없다.
			if (TestDropOnKill == true)
			{
				int count = Random.Range(TestDropCountMin, TestDropCountMax + 1);
				for (int i = 0; i < count; i++)
				{
					spawnDrop(DropObjectType.Item, randomAround(evt.Position));
				}
			}
		}

		// 보스 전멸기 파훼용 코어를 center 주변 radius 원주에 균등 배치한다.
		// 스폰된 코어를 outSpawned 에 담아 돌려주므로, 파훼가 끝나면 호출자가 Recall 로 회수한다.
		// (코어는 수명이 없다 — 회수 책임이 전환 시퀀스에 있다)
		public void SpawnGimmicks(Vector2 center, float radius, int count,
			IBossGimmickListener listener, List<BossGimmickCore> outSpawned)
		{
			if (count <= 0 || outSpawned == null)
			{
				return;
			}

			DropObjectPool pool;
			if (_pools.TryGetValue(DropObjectType.BossGimmick, out pool) == false || pool == null)
			{
				Debug.LogError("[DropManager] BossGimmick 풀이 없어 파훼 기믹을 생성하지 못했다 — 전멸기를 막을 수단이 사라진다.");
				return;
			}

			// 시작 각도를 무작위로 돌려 매번 같은 자리에 뜨지 않게 한다.
			float startAngle = Random.value * Mathf.PI * 2f;
			float step = Mathf.PI * 2f / count;

			for (int i = 0; i < count; i++)
			{
				float angle = startAngle + step * i;
				Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

				BossGimmickCore core = pool.Spawn(pos) as BossGimmickCore;
				if (core == null)
				{
					Debug.LogError("[DropManager] BossGimmick 프리팹에 BossGimmickCore 컴포넌트가 없다.");
					return;
				}

				core.SetListener(listener);
				outSpawned.Add(core);
			}
		}

		// 굴려 둔 처치 보상을 사망 위치 주변에 흩뿌린다. 보상 1건당 오브젝트 1개다.
		// 지급은 히어로가 획득 범위에 들어왔을 때 DropObject 가 한다.
		public void SpawnRewardDrops(Vector2 center, List<GrantedReward> rewards)
		{
			if (rewards == null || rewards.Count == 0)
			{
				return;
			}

			DropObjectPool pool;
			if (_pools.TryGetValue(DropObjectType.Item, out pool) == false || pool == null)
			{
				Debug.LogError("[DropManager] Item 풀이 없어 처치 보상을 떨어뜨리지 못했다 — 보상이 유실된다.");
				return;
			}

			for (int i = 0; i < rewards.Count; i++)
			{
				RewardDrop drop = pool.Spawn(randomAround(center)) as RewardDrop;
				if (drop == null)
				{
					Debug.LogError("[DropManager] Item 풀의 프리팹이 RewardDrop 이 아니다 — 보상이 유실된다.");
					continue;
				}

				_single.Clear();
				_single.Add(rewards[i]);
				drop.SetPayload(_single);
			}

			_single.Clear();
		}

		private void Update()
		{
			tickRuneSpawn();
		}

		// 히어로 주변에 랜덤 간격으로 버프룬을 놓는다. 풀이 없으면 아무 일도 하지 않는다.
		private void tickRuneSpawn()
		{
			if (_pools.ContainsKey(DropObjectType.BuffRune) == false)
			{
				return;
			}

			_runeTimer -= Time.deltaTime;
			if (_runeTimer > 0f)
			{
				return;
			}

			resetRuneTimer();

			UnitBase hero = findAliveHero();
			if (hero == null)
			{
				return;		// 히어로가 없거나 죽은 동안은 건너뛴다 — 다음 간격에 다시 시도한다
			}

			spawnDrop(DropObjectType.BuffRune, randomNear(hero.HitCenter));
		}

		private void resetRuneTimer()
		{
			_runeTimer = Random.Range(RuneIntervalMin, RuneIntervalMax);
		}

		private static UnitBase findAliveHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				if (heroes[i] != null && heroes[i].IsDead == false)
				{
					return heroes[i];
				}
			}

			return null;
		}

		// 지정 타입 드랍을 해당 위치에 그대로 스폰한다. 풀이 없으면 아무 일도 하지 않는다.
		private void spawnDrop(DropObjectType type, Vector2 pos)
		{
			DropObjectPool pool;
			if (_pools.TryGetValue(type, out pool) == false || pool == null)
			{
				return;
			}

			pool.Spawn(new Vector3(pos.x, pos.y, 0f));
		}

		private async UniTask createPoolAsync(DropObjectType type, string path, CancellationToken ct)
		{
			(bool canceled, GameObject prefab) = await ResourceManager.Instance.AcquireAsync<GameObject>(path, ct).SuppressCancellationThrow();
			if (canceled == true)
			{
				ct.ThrowIfCancellationRequested();
				return;
			}

			if (prefab == null)
			{
				Debug.LogError($"[DropManager] 드랍 프리팹 로드 실패: {path} (type={type})");
				return;
			}

			// await 사이 중복 생성 방지 가드 — 이미 만들어졌으면 방금 획득한 핸들만 해제
			if (_pools.ContainsKey(type) == true)
			{
				ResourceManager.Instance.Release(path);
				return;
			}

			GameObject go = new GameObject($"DropObjectPool_{type}");
			go.transform.SetParent(transform, false);
			go.SetActive(false);
			DropObjectPool pool = go.AddComponent<DropObjectPool>();
			pool.Setup(prefab, type, capacityFor(type));
			go.SetActive(true);

			_pools[type] = pool;
			_poolPaths[type] = path;
		}

		// 풀 GameObject 파괴(활성 드랍은 풀의 자식이라 함께 파괴됨) + 프리팹 Addressable 핸들 해제.
		private void clearPools()
		{
			Dictionary<DropObjectType, DropObjectPool>.Enumerator e = _pools.GetEnumerator();
			while (e.MoveNext())
			{
				DropObjectPool pool = e.Current.Value;
				if (pool != null)
				{
					Destroy(pool.gameObject);
				}
			}

			Dictionary<DropObjectType, string>.Enumerator pe = _poolPaths.GetEnumerator();
			while (pe.MoveNext())
			{
				ResourceManager.Instance.Release(pe.Current.Value);
			}

			_pools.Clear();
			_poolPaths.Clear();
		}

		private int capacityFor(DropObjectType type)
		{
			return _defaultPoolCapacity;
		}

		// 중심에서 RuneSpawnDist 범위의 랜덤 방향/거리 — 히어로 발밑에 겹쳐 뜨지 않게 한다.
		private static Vector2 randomNear(Vector2 center)
		{
			float angle = Random.Range(0f, Mathf.PI * 2f);
			float dist = Random.Range(RuneSpawnDistMin, RuneSpawnDistMax);
			return new Vector2(center.x + Mathf.Cos(angle) * dist, center.y + Mathf.Sin(angle) * dist);
		}

		private static Vector3 randomAround(Vector2 center)
		{
			Vector2 offset = Random.insideUnitCircle * ScatterRadius;
			return new Vector3(center.x + offset.x, center.y + offset.y, 0f);
		}
	}
}
