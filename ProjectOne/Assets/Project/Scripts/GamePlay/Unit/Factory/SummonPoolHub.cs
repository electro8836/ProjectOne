using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Resources;

namespace ProjectOne.Unit
{
	// MonsterPoolHub 와 같은 구조다 — 키만 EDT.Summon 이고 프리팹은 Table_Summon.PrefabPath 다.
	public sealed class SummonPoolHub : Singleton<SummonPoolHub>
	{
		private const int DefaultCapacity = 2;

		private SummonPoolHub()
		{
		}

		private readonly Dictionary<EDT.Summon, SummonPool> _pools = new Dictionary<EDT.Summon, SummonPool>();

		private readonly Dictionary<EDT.Summon, UniTaskCompletionSource<SummonPool>> _loading =
			new Dictionary<EDT.Summon, UniTaskCompletionSource<SummonPool>>();

		public async UniTask<SummonPool> GetOrCreatePoolAsync(EDT.Summon summonId, CancellationToken ct = default(CancellationToken))
		{
			SummonPool pool;
			if (_pools.TryGetValue(summonId, out pool) == true)
			{
				return pool;
			}

			UniTaskCompletionSource<SummonPool> inflight;
			if (_loading.TryGetValue(summonId, out inflight) == true)
			{
				return await inflight.Task.AttachExternalCancellation(ct);
			}

			inflight = new UniTaskCompletionSource<SummonPool>();
			_loading[summonId] = inflight;

			Table_Summon.Row row = Table_Summon.Get(summonId);
			if (row == null || string.IsNullOrEmpty(row.PrefabPath))
			{
				Debug.LogError($"[SummonPoolHub] Table_Summon 또는 PrefabPath 없음 (id={summonId})");
				_loading.Remove(summonId);
				inflight.TrySetResult(null);
				return null;
			}

			(bool loadCancelled, GameObject prefab) = await ResourceManager.Instance.AcquireAsync<GameObject>(row.PrefabPath, ct).SuppressCancellationThrow();
			if (loadCancelled)
			{
				_loading.Remove(summonId);
				inflight.TrySetCanceled();
				ct.ThrowIfCancellationRequested();
				return null;
			}

			if (prefab == null)
			{
				Debug.LogError("[SummonPoolHub] 프리팹 로드 실패: " + row.PrefabPath);
				_loading.Remove(summonId);
				inflight.TrySetResult(null);
				return null;
			}

			pool = createPool(prefab, summonId, row);
			_pools[summonId] = pool;
			_loading.Remove(summonId);
			inflight.TrySetResult(pool);
			return pool;
		}

		public SummonPool GetPool(EDT.Summon summonId)
		{
			SummonPool pool;
			_pools.TryGetValue(summonId, out pool);
			return pool;
		}

		// 씬 전환 시 호출 — 풀 GameObject 는 씬의 UnitContainer 자식이라 씬과 함께 파괴된다.
		// 캐시에 죽은 참조가 남으면 다음 씬에서 소환이 조용히 실패한다.
		public void Clear()
		{
			Dictionary<EDT.Summon, SummonPool>.KeyCollection.Enumerator e = _pools.Keys.GetEnumerator();
			while (e.MoveNext())
			{
				Table_Summon.Row row = Table_Summon.Get(e.Current);
				if (row != null && string.IsNullOrEmpty(row.PrefabPath) == false)
				{
					ResourceManager.Instance.Release(row.PrefabPath);
				}
			}

			_pools.Clear();
			_loading.Clear();
		}

		private SummonPool createPool(GameObject prefab, EDT.Summon summonId, Table_Summon.Row row)
		{
			GameObject val = new GameObject($"SummonPool_{summonId}");
			val.transform.SetParent(UnitManager.Instance.GetRoot(UnitType.Summon), false);
			val.SetActive(false);
			SummonPool pool = val.AddComponent<SummonPool>();
			pool.Setup(prefab, summonId, row, DefaultCapacity);
			val.SetActive(true);
			return pool;
		}
	}
}
