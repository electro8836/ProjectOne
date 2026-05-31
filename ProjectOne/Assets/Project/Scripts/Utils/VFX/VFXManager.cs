using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ProjectOne.Resources;

namespace ProjectOne.Utils
{
	// 주소(Addressable)별 VFX 인스턴스를 풀링하는 매니저.
	// - 비활성 인스턴스는 이 매니저의 자식으로 보관
	// - 재생 시 대상 transform 에 붙였다가, 반환되면 다시 매니저 자식으로 복귀
	// - 프리팹은 ResourceManager 로 지연 로드(최초 사용 시)한 뒤 캐시해 재사용
	public sealed class VFXManager : MonoSingleton<VFXManager>
	{
		// 주소별로 풀에 보관할 최대 비활성 인스턴스 수 (초과 반환분은 파괴) — PoolBase.maxCapacity 대응
		[SerializeField] private int _maxIdlePerAddress = 32;

		// 로드된 프리팹 (주소 → 프리팹). ResourceManager 로 Acquire, 매니저 수명 동안 유지
		private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

		// 주소별 비활성 인스턴스 풀
		private readonly Dictionary<string, Stack<VFXItem>> _pools = new Dictionary<string, Stack<VFXItem>>();

		// OnDestroy 시 Acquire 한 주소를 반환하기 위해 첫 로드 때 캐시 (Instance 게터 재생성 회피)
		private ResourceManager _resourceManager;

		// 매니저 파괴 진행 여부 — 비동기 콜백이 파괴 후 도착하는 경우 가드
		private bool _isQuitting;

		// ── 공개 API ──────────────────────────────────────────────────

		// one-shot : anchor 에 붙여(따라다님) 1회 재생, 파티클 수명 후 자동 반환 (SkillVFX 용)
		public void PlayOneShot(string address, Transform anchor)
		{
			if (string.IsNullOrEmpty(address) || anchor == null)
			{
				return;
			}

			playOneShotAsync(address, anchor, Vector3.zero, false).Forget();
		}

		// one-shot : 월드 좌표에 고정 소환(부착·추종 없음) (EffectVFX 용)
		public void PlayOneShot(string address, Vector3 worldPosition)
		{
			if (string.IsNullOrEmpty(address))
			{
				return;
			}

			playOneShotAsync(address, null, worldPosition, true).Forget();
		}

		// 루프성 : parent 에 붙여 유지, 핸들 반환 → 호출자가 Release (RootVFX)
		public VFXHandle Attach(string address, Transform parent)
		{
			VFXHandle handle = new VFXHandle(address, parent);
			if (string.IsNullOrEmpty(address) || parent == null)
			{
				handle.Released = true;
				return handle;
			}

			attachAsync(handle).Forget();
			return handle;
		}

		public void Release(VFXHandle handle)
		{
			if (handle == null || handle.Released == true)
			{
				return;
			}

			handle.Released = true;
			if (handle.Item != null)
			{
				ReturnToPool(handle.Item);
				handle.Item = null;
			}

			// Item == null 이면 아직 로드 중 → attachAsync 완료 콜백이 Released 를 보고 즉시 반환
		}

		// VFXItem 이 자동 반환(파티클 종료)하거나 내부에서 풀로 되돌릴 때 호출
		public void ReturnToPool(VFXItem item)
		{
			if (item == null)
			{
				return;
			}

			item.OnDeactivate();
			if (_isQuitting == true || this == null)
			{
				return;
			}

			Stack<VFXItem> pool;
			if (_pools.TryGetValue(item.Address, out pool) == false)
			{
				pool = new Stack<VFXItem>();
				_pools.Add(item.Address, pool);
			}

			// 상한 초과 → 풀에 넣지 않고 파괴 (PoolBase.maxCapacity 와 동일 정책)
			if (pool.Count >= _maxIdlePerAddress)
			{
				Destroy(item.gameObject);
				return;
			}

			item.transform.SetParent(transform, false);
			item.gameObject.SetActive(false);
			pool.Push(item);
		}

		// ── 비동기 스폰 ───────────────────────────────────────────────

		private async UniTask playOneShotAsync(string address, Transform anchor, Vector3 worldPosition, bool useWorld)
		{
			VFXItem item = await getItemAsync(address);
			if (item == null || _isQuitting == true)
			{
				return;
			}

			if (useWorld == true)
			{
				// 월드 고정 소환 — 부모는 매니저 그대로, 좌표만 지정 (대상 추종 안 함)
				item.transform.position = worldPosition;
			}
			else
			{
				// 비동기 로드 사이에 anchor 가 파괴됐을 수 있음
				if (anchor == null)
				{
					ReturnToPool(item);
					return;
				}

				item.transform.SetParent(anchor, false);
				item.transform.localPosition = Vector3.zero;
			}

			item.gameObject.SetActive(true);
			item.OnActivate();
			item.PlayOneShot();
		}

		private async UniTask attachAsync(VFXHandle handle)
		{
			VFXItem item = await getItemAsync(handle.Address);
			if (item == null || _isQuitting == true)
			{
				return;
			}

			// 로드 대기 중 Release 됐거나 부모가 파괴됨 → 즉시 반환
			if (handle.Released == true || handle.Parent == null)
			{
				ReturnToPool(item);
				return;
			}

			handle.Item = item;
			item.transform.SetParent(handle.Parent, false);
			item.transform.localPosition = Vector3.zero;
			item.gameObject.SetActive(true);
			item.OnActivate();
			item.PlayLooping();
		}

		// 풀에서 꺼내거나, 비어 있으면 프리팹 로드 후 새로 생성
		private async UniTask<VFXItem> getItemAsync(string address)
		{
			Stack<VFXItem> pool;
			if (_pools.TryGetValue(address, out pool) == true && pool.Count > 0)
			{
				return pool.Pop();
			}

			GameObject prefab = await getOrLoadPrefabAsync(address);
			if (prefab == null || _isQuitting == true)
			{
				return null;
			}

			// 로드 대기 중 다른 호출이 풀에 반환했을 수 있어 한 번 더 확인
			if (_pools.TryGetValue(address, out pool) == true && pool.Count > 0)
			{
				return pool.Pop();
			}

			return createItem(address, prefab);
		}

		private async UniTask<GameObject> getOrLoadPrefabAsync(string address)
		{
			GameObject prefab;
			if (_prefabs.TryGetValue(address, out prefab) == true)
			{
				return prefab;
			}

			if (_resourceManager == null)
			{
				_resourceManager = ResourceManager.Instance;
			}

			prefab = await _resourceManager.AcquireAsync<GameObject>(address);
			if (prefab == null)
			{
				return null;
			}

			// 동시 첫 로드로 다른 호출이 먼저 등록했으면 중복 Acquire 분을 되돌림 (refCount 보정)
			if (_prefabs.ContainsKey(address) == false)
			{
				_prefabs.Add(address, prefab);
			}
			else
			{
				_resourceManager.Release(address);
			}

			return prefab;
		}

		private VFXItem createItem(string address, GameObject prefab)
		{
			GameObject go = Instantiate(prefab, transform);
			VFXItem item = go.GetComponent<VFXItem>();
			if (item == null)
			{
				item = go.AddComponent<VFXItem>();
			}

			item.Initialize(this, address);
			go.SetActive(false);
			return item;
		}

		// ── 정리 ──────────────────────────────────────────────────────

		protected override void OnDestroy()
		{
			_isQuitting = true;

			// 캐시한 프리팹 주소마다 refCount 반환 (앱 종료 시엔 ResourceManager 가 이미 파괴됐을 수 있어 null 가드)
			if (_resourceManager != null)
			{
				List<string> keys = new List<string>(_prefabs.Keys);
				for (int i = 0; i < keys.Count; i++)
				{
					_resourceManager.Release(keys[i]);
				}
			}

			_prefabs.Clear();
			_pools.Clear();
			base.OnDestroy();
		}
	}
}
