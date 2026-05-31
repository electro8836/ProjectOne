using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Resources;

namespace ProjectOne.Unit
{
	public class MonsterPoolHub : MonoSingleton<MonsterPoolHub>
	{
		private const int DefaultCapacity = 4;

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
			try
			{
				Table_Monster.Row row = Table_Monster.Get(monsterId);
				if (row == null || string.IsNullOrEmpty(row.Path))
				{
					Debug.LogError($"[MonsterPoolHub] Table_Monster 또는 Path 없음 (id={monsterId})");
					_loading.Remove(monsterId);
					inflight.TrySetResult(null);
					return null;
				}

				GameObject val = await ResourceManager.Instance.AcquireAsync<GameObject>(row.Path, ct);
				if (val == null)
				{
					Debug.LogError(("[MonsterPoolHub] 프리팹 로드 실패: " + row.Path));
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
			catch (OperationCanceledException)
			{
				_loading.Remove(monsterId);
				inflight.TrySetCanceled();
				throw;
			}
		}

		public MonsterPool GetPool(int monsterId)
		{
			_pools.TryGetValue(monsterId, out var value);
			return value;
		}

		private MonsterPool createPool(GameObject prefab, int monsterId)
		{
			GameObject val = new GameObject($"MonsterPool_{monsterId}");
			val.transform.SetParent(this.transform, false);
			val.SetActive(false);
			MonsterPool monsterPool = val.AddComponent<MonsterPool>();
			monsterPool.Setup(prefab, monsterId, 4);
			val.SetActive(true);
			return monsterPool;
		}
	}
}
