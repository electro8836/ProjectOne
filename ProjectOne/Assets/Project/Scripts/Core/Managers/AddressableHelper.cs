using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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

		// ── 다운로드(원격 카탈로그) ───────────────────────────────────

		// key 또는 label에 대해 필요한 의존성 크기 (바이트)
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
		public static bool ReleaseInstance(GameObject instance)
		{
			if (instance == null)
			{
				return false;
			}

			if (_instanceHandles.TryGetValue(instance, out AsyncOperationHandle<GameObject> handle))
			{
				_instanceHandles.Remove(instance);
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
