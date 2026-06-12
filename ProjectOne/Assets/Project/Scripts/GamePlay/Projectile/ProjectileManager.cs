using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ProjectOne.Resources;
using ProjectOne.Utils;

namespace ProjectOne.Projectile
{
	// 발사체 프리팹 주소별 풀을 관리하고 발사하는 매니저 (SpawnProjectile 효과의 발사 진입점).
	// 프리팹은 ResourceManager 로 지연 로드(최초 사용 시)한 뒤 캐시 — VFXManager 와 동일 패턴.
	// 캐시 적중이면 동기 발사, 최초 로드가 필요할 때만 async 후 발사한다.
	public sealed class ProjectileManager : MonoSingleton<ProjectileManager>
	{
		[SerializeField] private int _poolCapacityPerPrefab = 16;

		// 주소별 발사체 풀 / 로드된 프리팹 / 로드 실패 주소(재시도·경고 폭주 방지)
		private readonly Dictionary<string, ProjectilePool> _pools = new Dictionary<string, ProjectilePool>();
		private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
		private readonly HashSet<string> _failedAddresses = new HashSet<string>();
		private bool _isQuitting;

		// 발사 — 캐시 적중이면 즉시, 최초 로드면 async 후 발사
		public void Launch(string address, ProjectileData data)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return;
			}

			ProjectilePool pool = tryGetPoolSync(address);
			if (pool != null)
			{
				pool.Spawn(data);
				return;
			}

			launchAsync(address, data).Forget();
		}

		// 풀이 이미 있거나 프리팹이 로드돼 있으면 동기로 풀 반환, 미로드면 null
		private ProjectilePool tryGetPoolSync(string address)
		{
			ProjectilePool pool;
			if (_pools.TryGetValue(address, out pool) == true)
			{
				return pool;
			}

			GameObject prefab;
			if (_prefabs.TryGetValue(address, out prefab) == true)
			{
				return createPool(address, prefab);
			}

			return null;
		}

		private async UniTask launchAsync(string address, ProjectileData data)
		{
			GameObject prefab = await getOrLoadPrefabAsync(address);
			if (prefab == null || _isQuitting == true)
			{
				return;
			}

			ProjectilePool pool;
			if (_pools.TryGetValue(address, out pool) == false)
			{
				pool = createPool(address, prefab);
			}

			pool.Spawn(data);
		}

		private async UniTask<GameObject> getOrLoadPrefabAsync(string address)
		{
			GameObject prefab;
			if (_prefabs.TryGetValue(address, out prefab) == true)
			{
				return prefab;
			}

			// 이전에 로드 실패한 잘못된 키는 재시도하지 않음
			if (_failedAddresses.Contains(address) == true)
			{
				return null;
			}

			// AcquireAsync 는 로드 실패 시 null 반환(내부 LogError) — 예외 없음
			prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address);
			if (prefab == null)
			{
				_failedAddresses.Add(address);
				Debug.LogWarning($"[ProjectileManager] 발사체 로드 실패 — address:{address}");
				return null;
			}

			// 동시 첫 로드로 다른 호출이 먼저 등록했으면 중복 Acquire 분을 되돌림 (refCount 보정)
			if (_prefabs.ContainsKey(address) == false)
			{
				_prefabs.Add(address, prefab);
			}
			else
			{
				ResourceManager.Instance.Release(address);
			}

			return prefab;
		}

		private ProjectilePool createPool(string address, GameObject prefab)
		{
			GameObject go = new GameObject($"ProjectilePool_{address}");
			go.transform.SetParent(transform, false);
			// Setup(프리팹/용량) 주입을 Awake/prewarm 전에 끝내기 위해 비활성 상태로 생성 후 활성화
			go.SetActive(false);
			ProjectilePool pool = go.AddComponent<ProjectilePool>();
			pool.Setup(prefab, _poolCapacityPerPrefab);
			go.SetActive(true);
			_pools.Add(address, pool);
			return pool;
		}

		protected override void OnDestroy()
		{
			_isQuitting = true;

			// 캐시한 프리팹 주소마다 refCount 반환 (앱 종료 시 ResourceManager 가 이미 파괴됐을 수 있어 null 가드)
			if (ResourceManager.HasInstance)
			{
				List<string> keys = new List<string>(_prefabs.Keys);
				for (int i = 0; i < keys.Count; i++)
				{
					ResourceManager.Instance.Release(keys[i]);
				}
			}

			_prefabs.Clear();
			_pools.Clear();
			_failedAddresses.Clear();
			base.OnDestroy();
		}
	}
}
