using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Resources;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 던전 드랍오브젝트 매니저(전투씬 수명). 스테이지 진입 시 해당 스테이지의
	// DropObjectGroupID 로 드랍 후보 행을 모으고 타입별 풀을 사전 생성한다.
	// 몬스터 사망(UnitDiedEvent) 시 죽은 몬스터의 MonsterType 과 일치하는 행을
	// DropChance 확률로 굴려 MinCount~MaxCount 개를 사망 위치에 스폰한다.
	// 단, MagicEssence(재화)는 1개만 스폰하고 MinCount~MaxCount 를 1회 획득량으로 쓴다.
	public sealed class DropManager : MonoSingleton<DropManager>
	{
		// 드랍 산포 반경 (사망 위치 주변)
		private const float ScatterRadius = 0.5f;

		[Header("풀 용량")]
		// MagicEssence 는 대량 드랍되므로 큰 용량
		[SerializeField] private int _essencePoolCapacity = 64;
		// 그 외 드랍 타입 기본 용량
		[SerializeField] private int _defaultPoolCapacity = 16;

		protected override bool Persistent => false;

		// 타입별 풀 / 풀 프리팹 주소(해제용)
		private readonly Dictionary<DropObjectType, DropObjectPool> _pools = new Dictionary<DropObjectType, DropObjectPool>();
		private readonly Dictionary<DropObjectType, string> _poolPaths = new Dictionary<DropObjectType, string>();

		// 현재 스테이지 드랍 후보 행 (그룹 매칭)
		private readonly List<Table_DropObject.Row> _currentGroupRows = new List<Table_DropObject.Row>();

		// Table_DropObject.All() 순회용 재사용 버퍼 (Dictionary 직접 순회 회피)
		private readonly List<Table_DropObject.Row> _allBuffer = new List<Table_DropObject.Row>();

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

		// 스테이지 진입 시 호출 — 이전 스테이지 풀을 정리하고 새 그룹의 후보 행 수집 + 타입별 풀 사전 생성.
		public async UniTask PrepareStageAsync(int groupId, CancellationToken ct)
		{
			clearPools();
			_currentGroupRows.Clear();

			if (groupId <= 0)
			{
				return;
			}

			// 그룹 매칭 행 수집
			_allBuffer.Clear();
			_allBuffer.AddRange(Table_DropObject.All().Values);
			for (int i = 0; i < _allBuffer.Count; i++)
			{
				Table_DropObject.Row row = _allBuffer[i];
				if (row.GroupID == groupId)
				{
					_currentGroupRows.Add(row);
				}
			}

			// 등장 타입별 풀 사전 생성 (첫 사망 시 히치 제거)
			for (int i = 0; i < _currentGroupRows.Count; i++)
			{
				Table_DropObject.Row row = _currentGroupRows[i];
				if (row.DropObjectType == DropObjectType.None || string.IsNullOrEmpty(row.Path))
				{
					continue;
				}

				if (_pools.ContainsKey(row.DropObjectType) == true)
				{
					continue;
				}

				await createPoolAsync(row.DropObjectType, row.Path, ct);
			}
		}

		// 던전 종료 시 호출 — 풀/후보 행 일괄 정리.
		public void Clear()
		{
			clearPools();
			_currentGroupRows.Clear();
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (evt.UnitType != UnitType.Monster)
			{
				return;
			}

			Table_Monster.Row monster = Table_Monster.Get(evt.TableID);
			if (monster == null)
			{
				return;
			}

			MonsterTypes mt = monster.MonsterType;
			for (int i = 0; i < _currentGroupRows.Count; i++)
			{
				Table_DropObject.Row row = _currentGroupRows[i];
				if (row.MonsterType != mt)
				{
					continue;
				}

				if (Random.Range(0f, 100f) >= row.DropChance)
				{
					continue;
				}

				DropObjectPool pool;
				if (_pools.TryGetValue(row.DropObjectType, out pool) == false || pool == null)
				{
					continue;
				}

				if (row.DropObjectType == DropObjectType.MagicEssence)
				{
					// 재화는 1개만 드랍하고, 개수 범위를 1회 획득량으로 사용
					int amount = Random.Range(row.MinCount, row.MaxCount + 1);
					pool.Spawn(randomAround(evt.Position), amount);
				}
				else
				{
					int count = Random.Range(row.MinCount, row.MaxCount + 1);
					for (int n = 0; n < count; n++)
					{
						pool.Spawn(randomAround(evt.Position), 1);
					}
				}
			}
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
			if (type == DropObjectType.MagicEssence)
			{
				return _essencePoolCapacity;
			}

			return _defaultPoolCapacity;
		}

		private static Vector3 randomAround(Vector2 center)
		{
			Vector2 offset = Random.insideUnitCircle * ScatterRadius;
			return new Vector3(center.x + offset.x, center.y + offset.y, 0f);
		}
	}
}
