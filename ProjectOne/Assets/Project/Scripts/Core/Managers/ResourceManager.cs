using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using ProjectOne.Utils;

namespace ProjectOne.Resources
{
	// Addressables 위에 얹는 참조카운트 캐시 매니저
	// - 같은 key를 여러 곳에서 Acquire/Release 해도 핸들은 한 번만 로드/해제됨
	// - 인스턴스(GameObject)는 캐시 대상이 아님 → AddressableHelper.InstantiateAsync 직접 사용
	// - 씬 전환 시 ReleaseAll로 일괄 정리 권장
	public class ResourceManager : MonoSingleton<ResourceManager>
	{
		// 캐시 엔트리: 에셋 본체 + 참조카운트 + 동시 로드 가드용 UniTaskCompletionSource
		private class Entry
		{
			public UnityEngine.Object asset;
			public int refCount;
			public UniTaskCompletionSource<UnityEngine.Object> loading; // 로드 중이면 non-null
		}

		private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

		protected override void OnDestroy()
		{
			releaseAllInternal();
			base.OnDestroy();
		}

		// ── 공개 API ──────────────────────────────────────────────────

		// 참조카운트 +1 후 에셋 반환. 같은 address 동시 호출도 안전.
		// 로드 실패 시 null 반환 (LogError는 내부 처리) — 취소 시 OCE 전파
		public async UniTask<T> AcquireAsync<T>(string address, CancellationToken ct = default)
			where T : UnityEngine.Object
		{
			if (string.IsNullOrEmpty(address))
			{
				Debug.LogError("[ResourceManager] address가 비어 있음");
				return null;
			}

			if (_entries.TryGetValue(address, out Entry entry))
			{
				// 다른 호출이 로드 중이면 그 결과를 공유
				if (entry.loading != null)
				{
					(bool waitCancelled, UnityEngine.Object loaded) = await entry.loading.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
					if (waitCancelled)
					{
						ct.ThrowIfCancellationRequested();
						return null;
					}

					if (loaded == null) { return null; }
					entry.refCount++;
					return loaded as T;
				}

				entry.refCount++;
				return entry.asset as T;
			}

			// 신규 로드 — 동시 호출 차단용 TCS 먼저 세팅
			entry = new Entry
			{
				refCount = 1,
				loading = new UniTaskCompletionSource<UnityEngine.Object>()
			};
			_entries[address] = entry;

			(bool loadCancelled, T asset) = await AddressableHelper.TryLoadAsync<T>(address, ct).SuppressCancellationThrow();
			if (loadCancelled)
			{
				entry.loading.TrySetCanceled();
				_entries.Remove(address);
				ct.ThrowIfCancellationRequested();
				return null;
			}

			if (asset == null)
			{
				// AddressableHelper.TryLoadAsync에서 이미 LogError 처리됨
				entry.loading.TrySetResult(null);
				_entries.Remove(address);
				return null;
			}

			entry.asset = asset;
			entry.loading.TrySetResult(asset);
			entry.loading = null;
			return asset;
		}

		// 참조카운트 -1, 0이 되면 실제 Addressables.Release
		public void Release(string address)
		{
			if (string.IsNullOrEmpty(address))
			{
				return;
			}

			if (!_entries.TryGetValue(address, out Entry entry))
			{
				return;
			}

			entry.refCount--;
			if (entry.refCount > 0)
			{
				return;
			}

			if (entry.asset != null)
			{
				AddressableHelper.ReleaseAsset(entry.asset);
			}

			_entries.Remove(address);
		}

		// 라벨 단위 사전 로드 (스플래시·로딩화면에서 사용)
		// 결과 리스트는 캐시에 들어가지 않음 — 호출자가 들고 있다가 ReleaseAssets로 반환
		public async UniTask<IList<T>> PreloadByLabelAsync<T>(
			string label,
			IProgress<float> progress = null,
			CancellationToken ct = default)
			where T : UnityEngine.Object
		{
			// 다운로드 → 로드 순서로 진행
			await AddressableHelper.DownloadDependenciesAsync(label, progress, ct);
			return await AddressableHelper.LoadAllByLabelAsync<T>(label, ct);
		}

		public void ReleasePreloaded<T>(IList<T> assets) where T : UnityEngine.Object
		{
			AddressableHelper.ReleaseAssets(assets);
		}

		// 캐시된 항목이 있는지 (디버그·UI 분기용)
		public bool IsCached(string address)
		{
			return _entries.TryGetValue(address, out Entry entry) && entry.asset != null;
		}

		public int GetRefCount(string address)
		{
			return _entries.TryGetValue(address, out Entry entry) ? entry.refCount : 0;
		}

		// 씬 전환 등에서 일괄 정리
		public void ReleaseAll()
		{
			releaseAllInternal();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void releaseAllInternal()
		{
			var keys = new List<string>(_entries.Keys);
			for (int i = 0; i < keys.Count; i++)
			{
				Entry entry = _entries[keys[i]];
				if (entry.asset != null)
				{
					AddressableHelper.ReleaseAsset(entry.asset);
				}
			}

			_entries.Clear();
		}
	}
}
