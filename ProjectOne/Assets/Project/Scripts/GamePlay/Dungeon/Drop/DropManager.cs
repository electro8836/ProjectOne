using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Resources;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 던전 드랍오브젝트 매니저(전투씬 수명). 타입별 풀 생성/정리와 사망 위치 스폰을 담당한다.
	//
	// TODO(STEP 10) — 드랍 규칙 미구현.
	// 구 Table_DropObject 가 사라지고 보상은 Reward / RewardItemPool 로 통합되었다.
	// 스테이지별 드랍 후보 수집과 확률 판정은 보상 시스템 작업에서 다시 붙인다.
	// 풀 생성·정리·산포 스폰 인프라는 그대로 재사용한다.
	public sealed class DropManager : MonoSingleton<DropManager>
	{
		// 드랍 산포 반경 (사망 위치 주변)
		private const float ScatterRadius = 0.5f;

		[Header("풀 용량")]
		[SerializeField] private int _defaultPoolCapacity = 16;

		protected override bool Persistent => false;

		// 타입별 풀 / 풀 프리팹 주소(해제용)
		private readonly Dictionary<DropObjectType, DropObjectPool> _pools = new Dictionary<DropObjectType, DropObjectPool>();
		private readonly Dictionary<DropObjectType, string> _poolPaths = new Dictionary<DropObjectType, string>();

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

		// 스테이지 진입 시 호출 — 이전 스테이지 풀을 정리한다.
		// 드랍 후보 수집과 풀 사전 생성은 STEP 10 에서 Reward 기반으로 다시 붙인다.
		public UniTask PrepareStageAsync(int groupId, CancellationToken ct)
		{
			clearPools();
			return UniTask.CompletedTask;
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

			// TODO(STEP 10) — 드랍 판정 미구현. spawnDrop 으로 사망 위치에 스폰한다.
		}

		// 지정 타입 드랍을 사망 위치 주변에 스폰한다. 풀이 없으면 아무 일도 하지 않는다.
		private void spawnDrop(DropObjectType type, Vector2 center)
		{
			DropObjectPool pool;
			if (_pools.TryGetValue(type, out pool) == false || pool == null)
			{
				return;
			}

			pool.Spawn(randomAround(center));
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

		private static Vector3 randomAround(Vector2 center)
		{
			Vector2 offset = Random.insideUnitCircle * ScatterRadius;
			return new Vector3(center.x + offset.x, center.y + offset.y, 0f);
		}
	}
}
