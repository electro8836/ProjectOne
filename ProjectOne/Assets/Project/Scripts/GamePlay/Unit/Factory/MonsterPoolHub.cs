using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Resources;

namespace ProjectOne.Unit
{
	public sealed class MonsterPoolHub : Singleton<MonsterPoolHub>
	{
		private const int DefaultCapacity = 4;

		private MonsterPoolHub()
		{
		}

		private readonly Dictionary<int, MonsterPool> _pools = new Dictionary<int, MonsterPool>();

		private readonly Dictionary<int, UniTaskCompletionSource<MonsterPool>> _loading = new Dictionary<int, UniTaskCompletionSource<MonsterPool>>();

		public async UniTask<MonsterPool> GetOrCreatePoolAsync(int monsterId, CancellationToken ct = default(CancellationToken))
		{
			if (_pools.TryGetValue(monsterId, out var value))
			{
				return value;
			}

			if (_loading.TryGetValue(monsterId, out var inflight))
			{
				return await inflight.Task.AttachExternalCancellation(ct);
			}

			inflight = new UniTaskCompletionSource<MonsterPool>();
			_loading[monsterId] = inflight;

			Table_Monster.Row row = Table_Monster.Get(monsterId);
			if (row == null || string.IsNullOrEmpty(row.PrefabPath))
			{
				Debug.LogError($"[MonsterPoolHub] Table_Monster 또는 PrefabPath 없음 (id={monsterId})");
				_loading.Remove(monsterId);
				inflight.TrySetResult(null);
				return null;
			}

			(bool loadCancelled, GameObject val) = await ResourceManager.Instance.AcquireAsync<GameObject>(row.PrefabPath, ct).SuppressCancellationThrow();
			if (loadCancelled)
			{
				_loading.Remove(monsterId);
				inflight.TrySetCanceled();
				ct.ThrowIfCancellationRequested();
				return null;
			}

			if (val == null)
			{
				Debug.LogError("[MonsterPoolHub] 프리팹 로드 실패: " + row.PrefabPath);
				_loading.Remove(monsterId);
				inflight.TrySetResult(null);
				return null;
			}

			MonsterPool monsterPool = createPool(val, monsterId);
			_pools[monsterId] = monsterPool;
			_loading.Remove(monsterId);
			inflight.TrySetResult(monsterPool);
			return monsterPool;
		}

		public MonsterPool GetPool(int monsterId)
		{
			_pools.TryGetValue(monsterId, out var value);
			return value;
		}

		// 전투 종료 시 호출 — 풀 GameObject 는 UnitContainer(전투씬 수명) 자식이라 씬과 함께 파괴된다.
		// 영속 허브의 캐시에 죽은 풀 참조가 남으면 다음 전투에서 재사용해 스폰이 조용히 실패하므로,
		// 캐시를 비우고 풀별 프리팹 Addressable 핸들을 해제(GetOrCreatePoolAsync 의 Acquire 와 짝)한다.
		public void Clear()
		{
			Dictionary<int, MonsterPool>.KeyCollection.Enumerator e = _pools.Keys.GetEnumerator();
			while (e.MoveNext())
			{
				Table_Monster.Row row = Table_Monster.Get(e.Current);
				if (row != null && string.IsNullOrEmpty(row.PrefabPath) == false)
				{
					ResourceManager.Instance.Release(row.PrefabPath);
				}
			}

			_pools.Clear();
			_loading.Clear();
		}

		private MonsterPool createPool(GameObject prefab, int monsterId)
		{
			GameObject val = new GameObject($"MonsterPool_{monsterId}");
			val.transform.SetParent(UnitContainer.Instance.GetRoot(UnitType.Monster), false);
			val.SetActive(false);
			MonsterPool monsterPool = val.AddComponent<MonsterPool>();
			monsterPool.Setup(prefab, monsterId, DefaultCapacity);
			val.SetActive(true);
			return monsterPool;
		}
	}
}
