using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace ProjectOne.Resources
{
	// Addressables 저수준 래퍼
	// - 핸들 추적: key(또는 인스턴스)로 Release 가능
	// - 캐싱/참조카운트는 ResourceManager 책임 (여기선 다루지 않음)
	// - 모든 비동기는 UniTask + CancellationToken 지원
	public static class AddressableHelper
	{
		// 로드된 에셋(또는 인스턴스) → 해당 핸들 매핑
		// key가 string이든 AssetReference든 결과 오브젝트로 역참조해 Release 한다
		private static readonly Dictionary<object, AsyncOperationHandle> _assetHandles
			= new Dictionary<object, AsyncOperationHandle>();

		private static readonly Dictionary<GameObject, AsyncOperationHandle<GameObject>> _instanceHandles
			= new Dictionary<GameObject, AsyncOperationHandle<GameObject>>();

		// ── 에셋 로드 ─────────────────────────────────────────────────

		// 주소 문자열로 에셋 로드. 실패 시 예외.
		public static async UniTask<T> LoadAsync<T>(string address, CancellationToken ct = default)
			where T : UnityEngine.Object
		{
			if (string.IsNullOrEmpty(address))
			{
				throw new ArgumentException("address가 비어 있음", nameof(address));
			}

			AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
			return await awaitAssetHandle(handle, address, ct);
		}

		// LoadAsync 실패를 null 반환으로 변환 (호출부 try-catch 제거용). OCE는 그대로 전파.
		public static async UniTask<T> TryLoadAsync<T>(string address, CancellationToken ct = default)
			where T : UnityEngine.Object
		{
			try
			{
				return await LoadAsync<T>(address, ct);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				Debug.LogError($"[AddressableHelper] 에셋 로드 실패: {address} ({e.Message})");
				return null;
			}
		}

		// AssetReference 로 에셋 로드. 빌드 시 안전(주소 오타 방지).
		public static async UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken ct = default)
			where T : UnityEngine.Object
		{
			if (reference == null || !reference.RuntimeKeyIsValid())
			{
				throw new ArgumentException("AssetReference가 유효하지 않음", nameof(reference));
			}

			AsyncOperationHandle<T> handle = reference.LoadAssetAsync<T>();
			return await awaitAssetHandle(handle, reference.RuntimeKey, ct);
		}

		// 라벨로 묶인 모든 에셋 로드 (예: "EnemyPrefabs")
		public static async UniTask<IList<T>> LoadAllByLabelAsync<T>(string label, CancellationToken ct = default)
			where T : UnityEngine.Object
		{
			if (string.IsNullOrEmpty(label))
			{
				throw new ArgumentException("label이 비어 있음", nameof(label));
			}

			AsyncOperationHandle<IList<T>> handle = Addressables.LoadAssetsAsync<T>(label, null);
			bool succeeded = false;
			try
			{
				await handle.ToUniTask(cancellationToken: ct);
				succeeded = true;
			}
			finally
			{
				if (!succeeded && handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Exception err = handle.OperationException;
				Addressables.Release(handle);
				throw new Exception($"라벨 로드 실패: {label}", err);
			}

			// 라벨 결과는 IList 자체에 핸들을 묶어둠
			_assetHandles[handle.Result] = handle;
			return handle.Result;
		}

		// ── 인스턴스화 ────────────────────────────────────────────────

		public static async UniTask<GameObject> InstantiateAsync(
			string address,
			Transform parent = null,
			bool instantiateInWorldSpace = false,
			CancellationToken ct = default)
		{
			if (string.IsNullOrEmpty(address))
			{
				throw new ArgumentException("address가 비어 있음", nameof(address));
			}

			AsyncOperationHandle<GameObject> handle =
				Addressables.InstantiateAsync(address, parent, instantiateInWorldSpace);
			return await awaitInstanceHandle(handle, address, ct);
		}

		// InstantiateAsync 실패를 null 반환으로 변환 (호출부 try-catch 제거용). OCE는 그대로 전파.
		// 미등록 주소가 흔한 경로 — 콘텐츠가 없다고 흐름 전체가 죽어서는 안 되는 곳 — 가 쓴다.
		public static async UniTask<GameObject> TryInstantiateAsync(
			string address,
			Transform parent = null,
			bool instantiateInWorldSpace = false,
			CancellationToken ct = default)
		{
			if (string.IsNullOrEmpty(address))
			{
				return null;
			}

			// 미등록 주소는 콘텐츠 제작 중 흔한 상태다. 예외를 던지기 전에 걸러 콘솔을 조용히 유지한다.
			if (HasKey(address) == false)
			{
				Debug.LogWarning($"[AddressableHelper] 카탈로그에 없는 주소입니다: {address} — 인스턴스화를 건너뜁니다.");
				return null;
			}

			try
			{
				return await InstantiateAsync(address, parent, instantiateInWorldSpace, ct);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				Debug.LogError($"[AddressableHelper] 인스턴스화 실패: {address} ({e.Message})");
				return null;
			}
		}

		public static async UniTask<GameObject> InstantiateAsync(
			AssetReference reference,
			Transform parent = null,
			bool instantiateInWorldSpace = false,
			CancellationToken ct = default)
		{
			if (reference == null || !reference.RuntimeKeyIsValid())
			{
				throw new ArgumentException("AssetReference가 유효하지 않음", nameof(reference));
			}

			AsyncOperationHandle<GameObject> handle =
				reference.InstantiateAsync(parent, instantiateInWorldSpace);
			return await awaitInstanceHandle(handle, reference.RuntimeKey, ct);
		}

		// ── 씬 로드 ───────────────────────────────────────────────────

		public static async UniTask<SceneInstance> LoadSceneAsync(
			string address,
			LoadSceneMode mode = LoadSceneMode.Single,
			bool activateOnLoad = true,
			CancellationToken ct = default)
		{
			if (string.IsNullOrEmpty(address))
			{
				throw new ArgumentException("address가 비어 있음", nameof(address));
			}

			AsyncOperationHandle<SceneInstance> handle =
				Addressables.LoadSceneAsync(address, mode, activateOnLoad);

			// 씬 비활성/해제 처리는 호출자 책임 — 취소/실패 시 그대로 전파
			await handle.ToUniTask(cancellationToken: ct);

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				throw new Exception($"씬 로드 실패: {address}", handle.OperationException);
			}

			return handle.Result;
		}

		public static async UniTask UnloadSceneAsync(SceneInstance scene, CancellationToken ct = default)
		{
			AsyncOperationHandle<SceneInstance> handle = Addressables.UnloadSceneAsync(scene);
			await handle.ToUniTask(cancellationToken: ct);
		}

		// ── 키 존재 확인 ──────────────────────────────────────────────

		// 카탈로그에 이 키(주소 또는 라벨)의 위치가 있는가.
		//
		// try-catch 로 감싸는 것만으로는 부족하다 — Addressables 는 예외를 던지기 **전에**
		// 자체적으로 LogException 을 찍으므로 콘솔에 붉은 에러가 그대로 남는다.
		// 없는 것이 정상인 경로(빈 그룹의 라벨, 미제작 프리팹)는 호출 자체를 하지 않아야 조용하다.
		public static bool HasKey(object key)
		{
			if (key == null)
			{
				return false;
			}

			IEnumerator<IResourceLocator> e = Addressables.ResourceLocators.GetEnumerator();
			while (e.MoveNext() == true)
			{
				IList<IResourceLocation> locations;
				if (e.Current.Locate(key, null, out locations) == true && locations != null && locations.Count > 0)
				{
					return true;
				}
			}

			return false;
		}

		// ── 다운로드(원격 카탈로그) ───────────────────────────────────

		// key 또는 label 의 다운로드 크기(바이트) 조회.
		// GetDownloadSizeAsync 실패를 0 반환으로 변환 (호출부 try-catch 제거용). OCE는 그대로 전파.
		//
		// 그룹이 비어 있으면 그 라벨은 카탈로그에 위치가 없어 InvalidKeyException 이 난다.
		// 콘텐츠 제작 중에는 흔한 상태이고, 라벨 하나 때문에 패치 단계가 통째로 죽으면 게임을 켤 수 없다.
		// 크기 0 이면 호출부가 자연히 그 라벨을 건너뛴다.
		public static async UniTask<long> TryGetDownloadSizeAsync(object key, CancellationToken ct = default)
		{
			// 조회 전에 막는다 — 예외를 잡아도 Addressables 가 이미 콘솔에 에러를 찍은 뒤다.
			if (HasKey(key) == false)
			{
				Debug.LogWarning($"[AddressableHelper] 카탈로그에 없는 키입니다: {key} — 다운로드를 건너뜁니다(그룹이 비었을 수 있음).");
				return 0L;
			}

			try
			{
				return await GetDownloadSizeAsync(key, ct);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AddressableHelper] 다운로드 크기 조회 실패: {key} ({e.Message}) — 해당 라벨을 건너뜁니다.");
				return 0L;
			}
		}

		// 실패 시 예외를 던진다. 라벨 존재가 보장된 곳에서 쓴다 —
		// 조용한 0바이트가 오히려 위험한 경우를 위해 남겨 둔다.
		public static async UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct = default)
		{
			AsyncOperationHandle<long> handle = Addressables.GetDownloadSizeAsync(key);
			bool succeeded = false;
			try
			{
				await handle.ToUniTask(cancellationToken: ct);
				succeeded = true;
			}
			finally
			{
				if (!succeeded && handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}

			long size = handle.Result;
			Addressables.Release(handle);
			return size;
		}

		// 다운로드 성공 여부를 bool로 반환 — 실패 시 LogWarning 후 false 반환, 취소는 OCE 재전파
		public static async UniTask<bool> TryDownloadDependenciesAsync(
			object key,
			IProgress<float> progress = null,
			CancellationToken ct = default)
		{
			AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(key, false);
			try
			{
				await handle.ToUniTask(progress: progress, cancellationToken: ct);
				if (handle.IsValid()) { Addressables.Release(handle); }
				return true;
			}
			catch (OperationCanceledException)
			{
				if (handle.IsValid()) { Addressables.Release(handle); }
				throw;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AddressableHelper] 다운로드 실패: {key} ({e.Message})");
				if (handle.IsValid()) { Addressables.Release(handle); }
				return false;
			}
		}

		// 의존성 다운로드 (진행률 콜백 옵션)
		public static async UniTask DownloadDependenciesAsync(
			object key,
			IProgress<float> progress = null,
			CancellationToken ct = default)
		{
			AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(key, false);
			try
			{
				await handle.ToUniTask(progress: progress, cancellationToken: ct);
			}
			finally
			{
				if (handle.IsValid())
				{
					// 다운로드 핸들은 즉시 Release 해도 캐시는 유지됨
					Addressables.Release(handle);
				}
			}
		}

		// ── 해제 ──────────────────────────────────────────────────────

		// 로드한 에셋 해제 (LoadAsync 결과 그대로 넘김)
		public static void ReleaseAsset(UnityEngine.Object asset)
		{
			if (asset == null)
			{
				return;
			}

			if (_assetHandles.TryGetValue(asset, out AsyncOperationHandle handle))
			{
				_assetHandles.Remove(asset);
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}
		}

		// 라벨 로드 결과(IList) 해제
		public static void ReleaseAssets<T>(IList<T> assets) where T : UnityEngine.Object
		{
			if (assets == null)
			{
				return;
			}

			if (_assetHandles.TryGetValue(assets, out AsyncOperationHandle handle))
			{
				_assetHandles.Remove(assets);
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}
		}

		// InstantiateAsync로 만든 인스턴스 해제 (GameObject.Destroy 대신 사용)
		// 인스턴스 해제. **이미 파괴된 인스턴스도 처리한다.**
		//
		// 씬 언로드가 인스턴스를 먼저 파괴하면 Addressables 는 그것을 더 이상 찾지 못해
		// 핸들이 영원히 남는다(번들 참조카운트가 떨어지지 않는다).
		// 파괴된 유니티 오브젝트도 딕셔너리 키로는 유효하므로, 추적해 둔 핸들로 직접 해제한다.
		//
		// 진입 가드에 ReferenceEquals 를 쓰는 이유 — 일반 null 비교는 "인자가 진짜 null" 과
		// "파괴된 오브젝트"를 구분하지 못해 후자까지 걸러내 버린다.
		public static bool ReleaseInstance(GameObject instance)
		{
			if (ReferenceEquals(instance, null) == true)
			{
				return false;
			}

			bool tracked = _instanceHandles.TryGetValue(instance, out AsyncOperationHandle<GameObject> handle);
			if (tracked == true)
			{
				_instanceHandles.Remove(instance);
			}

			// 여기서의 null 은 "파괴됨" 을 뜻한다(위에서 진짜 null 은 걸러냈다).
			if (instance == null)
			{
				if (tracked == true && handle.IsValid() == true)
				{
					Addressables.Release(handle);
					return true;
				}

				return false;
			}

			return Addressables.ReleaseInstance(instance);
		}

		// 추적 중인 모든 핸들 해제 (씬 전환 등 일괄 정리)
		public static void ReleaseAll()
		{
			var assetKeys = new List<object>(_assetHandles.Keys);
			for (int i = 0; i < assetKeys.Count; i++)
			{
				AsyncOperationHandle handle = _assetHandles[assetKeys[i]];
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}

			_assetHandles.Clear();

			var instances = new List<GameObject>(_instanceHandles.Keys);
			for (int i = 0; i < instances.Count; i++)
			{
				GameObject go = instances[i];
				if (go != null)
				{
					Addressables.ReleaseInstance(go);
				}
			}

			_instanceHandles.Clear();
		}

		// ── 내부 공통 ─────────────────────────────────────────────────

		private static async UniTask<T> awaitAssetHandle<T>(
			AsyncOperationHandle<T> handle,
			object key,
			CancellationToken ct)
			where T : UnityEngine.Object
		{
			bool succeeded = false;
			try
			{
				await handle.ToUniTask(cancellationToken: ct);
				succeeded = true;
			}
			finally
			{
				if (!succeeded && handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}

			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Exception err = handle.OperationException;
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}

				throw new Exception($"에셋 로드 실패: {key}", err);
			}

			_assetHandles[handle.Result] = handle;
			return handle.Result;
		}

		private static async UniTask<GameObject> awaitInstanceHandle(
			AsyncOperationHandle<GameObject> handle,
			object key,
			CancellationToken ct)
		{
			bool succeeded = false;
			try
			{
				await handle.ToUniTask(cancellationToken: ct);
				succeeded = true;
			}
			finally
			{
				if (!succeeded && handle.IsValid())
				{
					if (handle.Result != null)
					{
						Addressables.ReleaseInstance(handle.Result);
					}
					else
					{
						Addressables.Release(handle);
					}
				}
			}

			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Exception err = handle.OperationException;
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}

				throw new Exception($"인스턴스화 실패: {key}", err);
			}

			_instanceHandles[handle.Result] = handle;
			return handle.Result;
		}
	}
}
