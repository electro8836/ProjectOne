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

		// 로드에 실패한(잘못된 Addressable 키) 주소 — 재시도/경고 폭주 방지용 블랙리스트
		private readonly HashSet<string> _failedAddresses = new HashSet<string>();

		// 활성 one-shot — 매 프레임 중앙 틱(Update)에서 종료 감지 후 풀로 회수
		private readonly List<VFXItem> _activeOneShots = new List<VFXItem>(64);

		// 매니저 파괴 진행 여부 — 비동기 콜백이 파괴 후 도착하는 경우 가드
		private bool _isQuitting;

		// ── 공개 API ──────────────────────────────────────────────────

		// one-shot : anchor 에 붙여(따라다님) 1회 재생, 파티클 수명 후 자동 반환 (SkillVFX 용)
		public void PlayOneShot(string address, Transform anchor)
		{
			PlayOneShot(address, anchor, Vector3.zero);
		}

		// one-shot : anchor 에 붙여 localOffset 만큼 띄워 1회 재생 (Center 앵커 등)
		public void PlayOneShot(string address, Transform anchor, Vector3 localOffset)
		{
			if (string.IsNullOrEmpty(address) || anchor == null)
			{
				return;
			}

			// 캐시 적중이면 동기 스폰 (비동기 우회), 최초 로드 필요할 때만 async
			VFXItem item = tryGetItemSync(address);
			if (item != null)
			{
				activateOneShot(item, anchor, localOffset, Vector3.zero, false);
				return;
			}

			playOneShotAsync(address, anchor, localOffset, Vector3.zero, false).Forget();
		}

		// one-shot : 월드 좌표에 고정 소환(부착·추종 없음) (EffectVFX 용)
		public void PlayOneShot(string address, Vector3 worldPosition)
		{
			if (string.IsNullOrEmpty(address))
			{
				return;
			}

			VFXItem item = tryGetItemSync(address);
			if (item != null)
			{
				activateOneShot(item, null, Vector3.zero, worldPosition, true);
				return;
			}

			playOneShotAsync(address, null, Vector3.zero, worldPosition, true).Forget();
		}

		// 루프성 : parent 에 붙여 유지, 핸들 반환 → 호출자가 Release (RootVFX)
		public VFXHandle Attach(string address, Transform parent)
		{
			return Attach(address, parent, Vector3.zero);
		}

		// 루프성 : parent 에 localOffset 만큼 띄워 부착 유지 (Center 앵커 등)
		public VFXHandle Attach(string address, Transform parent, Vector3 localOffset)
		{
			VFXHandle handle = new VFXHandle(address, parent);
			handle.LocalOffset = localOffset;
			if (string.IsNullOrEmpty(address) || parent == null)
			{
				handle.Released = true;
				return handle;
			}

			VFXItem item = tryGetItemSync(address);
			if (item != null)
			{
				activateLooping(handle, item);
				return handle;
			}

			attachAsync(handle).Forget();
			return handle;
		}

		// ── 중앙 틱 (one-shot 종료 감지·회수) ─────────────────────────

		private void Update()
		{
			// 뒤에서부터 swap-remove — 종료/파괴된 one-shot 을 풀로 회수
			for (int i = _activeOneShots.Count - 1; i >= 0; i--)
			{
				VFXItem item = _activeOneShots[i];
				if (item == null)
				{
					removeActiveAt(i);
					continue;
				}

				if (item.IsFinished() == true)
				{
					removeActiveAt(i);
					ReturnToPool(item);
				}
			}
		}

		private void removeActiveAt(int index)
		{
			int last = _activeOneShots.Count - 1;
			_activeOneShots[index] = _activeOneShots[last];
			_activeOneShots.RemoveAt(last);
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

		// ── 스폰 활성화 (동기·비동기 공유) ────────────────────────────

		// one-shot 활성: 위치 지정 → 활성 → 재생 → 활성 리스트 등록
		private void activateOneShot(VFXItem item, Transform anchor, Vector3 localOffset, Vector3 worldPosition, bool useWorld)
		{
			if (useWorld == true)
			{
				// 월드 고정 소환 — 부모는 매니저 그대로, 좌표만 지정 (대상 추종 안 함)
				item.transform.position = worldPosition;
			}
			else
			{
				item.transform.SetParent(anchor, false);
				item.transform.localPosition = localOffset;
			}

			item.gameObject.SetActive(true);
			item.OnActivate();
			item.PlayOneShot();
			_activeOneShots.Add(item);
		}

		// 루프성 활성: 핸들에 연결 → parent 부착 → 재생 (회수는 Release 로만)
		private void activateLooping(VFXHandle handle, VFXItem item)
		{
			// 로드 대기 중 Release 됐거나 부모가 파괴됨 → 즉시 반환
			if (handle.Released == true || handle.Parent == null)
			{
				ReturnToPool(item);
				return;
			}

			handle.Item = item;
			item.transform.SetParent(handle.Parent, false);
			item.transform.localPosition = handle.LocalOffset;
			item.gameObject.SetActive(true);
			item.OnActivate();
			item.PlayLooping();
		}

		// ── 비동기 스폰 (최초 로드 필요 시에만) ───────────────────────

		private async UniTask playOneShotAsync(string address, Transform anchor, Vector3 localOffset, Vector3 worldPosition, bool useWorld)
		{
			VFXItem item = await getItemAsync(address);
			if (item == null || _isQuitting == true)
			{
				return;
			}

			// 비동기 로드 사이에 anchor 가 파괴됐을 수 있음
			if (useWorld == false && anchor == null)
			{
				ReturnToPool(item);
				return;
			}

			activateOneShot(item, anchor, localOffset, worldPosition, useWorld);
		}

		private async UniTask attachAsync(VFXHandle handle)
		{
			VFXItem item = await getItemAsync(handle.Address);
			if (item == null || _isQuitting == true)
			{
				return;
			}

			activateLooping(handle, item);
		}

		// 풀에 여유가 있거나 프리팹이 이미 로드돼 있으면 동기로 인스턴스 반환, 미로드면 null
		private VFXItem tryGetItemSync(string address)
		{
			Stack<VFXItem> pool;
			if (_pools.TryGetValue(address, out pool) == true && pool.Count > 0)
			{
				return pool.Pop();
			}

			GameObject prefab;
			if (_prefabs.TryGetValue(address, out prefab) == true)
			{
				return createItem(address, prefab);
			}

			return null;
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

			// 이전에 로드 실패한 잘못된 키는 재시도하지 않음
			if (_failedAddresses.Contains(address) == true)
			{
				return null;
			}

			// AcquireAsync는 로드 실패 시 null 반환(내부 LogError 처리) — 예외 없음
			prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address);
			if (prefab == null)
			{
				_failedAddresses.Add(address);
				Debug.LogWarning($"[VFXManager] VFX 로드 실패 — address:{address}");
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

		private VFXItem createItem(string address, GameObject prefab)
		{
			GameObject go = Instantiate(prefab, transform);
			VFXItem item = go.GetComponent<VFXItem>();
			if (item == null)
			{
				item = go.AddComponent<VFXItem>();
			}

			item.Initialize(address);
			go.SetActive(false);
			return item;
		}

		// ── 정리 ──────────────────────────────────────────────────────

		protected override void OnDestroy()
		{
			_isQuitting = true;

			// 캐시한 프리팹 주소마다 refCount 반환 (앱 종료 시엔 ResourceManager 가 이미 파괴됐을 수 있어 null 가드)
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
			_activeOneShots.Clear();
			_failedAddresses.Clear();
			base.OnDestroy();
		}
	}
}
