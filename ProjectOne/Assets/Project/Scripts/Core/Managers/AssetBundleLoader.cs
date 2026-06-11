using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using ProjectOne.Utils;

namespace ProjectOne.Resources
{
	// 번들 다운로드 진행 정보 — IProgress<T>로 전달
	public readonly struct BundleDownloadProgress
	{
		public readonly long downloadedBytes;
		public readonly long totalBytes;
		public readonly float ratio;           // 0~1
		public readonly float speedBytesPerSec;

		public BundleDownloadProgress(long downloaded, long total, float speed)
		{
			downloadedBytes = downloaded;
			totalBytes = total;
			ratio = total > 0 ? (float)downloaded / total : 0f;
			speedBytesPerSec = speed;
		}
	}

	// 업데이트 체크 결과
	public readonly struct BundleCheckResult
	{
		public readonly bool hasUpdate;
		public readonly long totalBytes;

		public BundleCheckResult(long bytes)
		{
			totalBytes = bytes;
			hasUpdate = bytes > 0;
		}
	}

	// Addressables 에셋번들 다운로드 매니저
	// - PatchConfig를 [SerializeField]로 직접 보유 (인스펙터 주입) → 부트 씬에 GameObject 배치
	// - 서버 URL 런타임 설정 (InternalIdTransformFunc 방식)
	// - 카탈로그 갱신 → 다운로드 크기 체크 → 번들 다운로드
	// - 재시도(지수 백오프) + label 단위 완료 기록(PlayerPrefs)으로 이어받기 지원
	//   · 번들 단위: Unity 캐시가 완료된 번들을 자동으로 보존 — GetDownloadSizeAsync가 0 반환
	//   · label 단위: 완료된 label을 PlayerPrefs에 기록 → 앱 재시작 후 건너뜀
	public class AssetBundleLoader : MonoSingleton<AssetBundleLoader>
	{
		private const string _doneKeyPrefix = "ABL_Done_";
		private const int _baseRetryDelayMs = 1000;

		// 패치 부트 설정 — 인스펙터에서 PatchConfig.asset 연결
		[SerializeField] private PatchConfig _config;
		public PatchConfig Config => _config;

		private string _serverUrl;

		// 속도 측정용 상태 (DownloadAsync 진입마다 초기화)
		private float _lastSpeedSampleTime;
		private long _lastSpeedSampleBytes;
		private float _lastSpeed;

		// ── 설정 ─────────────────────────────────────────────────────────

		// 번들 서버 주소 설정. null/빈 문자열이면 기본 Addressables URL 사용.
		// 원격 리소스 URL의 호스트+스킴을 런타임에 치환한다.
		public void SetServerUrl(string url)
		{
			_serverUrl = url;
			Addressables.InternalIdTransformFunc = string.IsNullOrEmpty(url) ? null : transformInternalId;
		}

		// ── 카탈로그 갱신 ─────────────────────────────────────────────────

		// 원격 카탈로그가 변경됐는지 확인 → 갱신 필요한 카탈로그 id 목록 반환
		public async UniTask<IReadOnlyList<string>> CheckCatalogUpdateAsync(CancellationToken ct = default)
		{
			AsyncOperationHandle<List<string>> handle = Addressables.CheckForCatalogUpdates(false);
			bool succeeded = false;
			try
			{
				await handle.ToUniTask(cancellationToken: ct);
				succeeded = true;
			}
			finally
			{
				if (!succeeded && handle.IsValid()) { Addressables.Release(handle); }
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Exception err = handle.OperationException;
				if (handle.IsValid()) { Addressables.Release(handle); }
				throw new Exception("카탈로그 업데이트 확인 실패", err);
			}

			List<string> result = handle.Result;
			Addressables.Release(handle);
			return result ?? (IReadOnlyList<string>)Array.Empty<string>();
		}

		// 변경된 카탈로그 갱신 (CheckCatalogUpdateAsync 반환값을 그대로 넘김)
		public async UniTask UpdateCatalogsAsync(IReadOnlyList<string> catalogIds, CancellationToken ct = default)
		{
			if (catalogIds == null || catalogIds.Count == 0) { return; }

			AsyncOperationHandle<List<IResourceLocator>> handle = Addressables.UpdateCatalogs(catalogIds, false);
			bool succeeded = false;
			try
			{
				await handle.ToUniTask(cancellationToken: ct);
				succeeded = true;
			}
			finally
			{
				if (!succeeded && handle.IsValid()) { Addressables.Release(handle); }
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Exception err = handle.OperationException;
				if (handle.IsValid()) { Addressables.Release(handle); }
				throw new Exception("카탈로그 갱신 실패", err);
			}

			Addressables.Release(handle);
		}

		// ── 크기 체크 ─────────────────────────────────────────────────────

		// label 목록 기준으로 로컬 캐시에 없는 총 다운로드 크기 반환
		public async UniTask<BundleCheckResult> CheckUpdateAsync(IEnumerable<string> labels, CancellationToken ct = default)
		{
			var list = new List<string>(labels);
			long total = 0;
			for (int i = 0; i < list.Count; i++)
			{
				long size = await AddressableHelper.GetDownloadSizeAsync(list[i], ct);
				total += size;
			}
			return new BundleCheckResult(total);
		}

		// ── 스토리지 ──────────────────────────────────────────────────────

		// Application.persistentDataPath 드라이브의 여유 공간 (바이트).
		// 조회 실패 시 long.MaxValue 반환 — 다운로드 전 사전 확인용.
		public long GetAvailableStorageBytes()
		{
			string root = Path.GetPathRoot(Application.persistentDataPath);
			if (string.IsNullOrEmpty(root))
			{
				Debug.LogWarning("[AssetBundleLoader] 저장소 루트 경로 확인 실패");
				return long.MaxValue;
			}

			DriveInfo drive = new DriveInfo(root);
			if (!drive.IsReady)
			{
				Debug.LogWarning($"[AssetBundleLoader] 드라이브 준비 안 됨: {root}");
				return long.MaxValue;
			}

			return drive.AvailableFreeSpace;
		}

		// ── 다운로드 ──────────────────────────────────────────────────────

		// label 목록을 순서대로 다운로드.
		// - 완료 표시된 label은 건너뜀 (앱 재시작 후 이어받기)
		// - 네트워크 오류 시 maxRetry 이내 지수 백오프 재시도
		// - 전체 완료 후 PlayerPrefs 완료 기록 삭제 (다음 패치 사이클 대비)
		public async UniTask DownloadAsync(
			IEnumerable<string> labels,
			IProgress<BundleDownloadProgress> progress = null,
			int maxRetry = 3,
			CancellationToken ct = default)
		{
			var list = new List<string>(labels);

			// 크기 사전 조회 — 완료 표시된 label은 0으로 취급
			long[] sizes = new long[list.Count];
			long totalBytes = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (!isLabelDone(list[i]))
				{
					sizes[i] = await AddressableHelper.GetDownloadSizeAsync(list[i], ct);
					totalBytes += sizes[i];
				}
			}

			// 속도 계산 상태 초기화
			_lastSpeedSampleTime = Time.realtimeSinceStartup;
			_lastSpeedSampleBytes = 0;
			_lastSpeed = 0f;

			long downloadedBytes = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (isLabelDone(list[i])) { continue; }

				if (sizes[i] > 0)
				{
					var reporter = new LabelProgressReporter(this, sizes[i], downloadedBytes, totalBytes, progress);
					bool ok = await downloadLabelAsync(list[i], reporter, maxRetry, ct);
					if (!ok) { return; }
					downloadedBytes += sizes[i];
				}

				markLabelDone(list[i]);
			}

			ClearDownloadRecords(list);

			if (progress != null)
			{
				progress.Report(new BundleDownloadProgress(totalBytes, totalBytes, 0f));
			}
		}

		// 지정한 label들의 완료 기록 삭제 (다음 패치 사이클 전 수동 호출 가능)
		public void ClearDownloadRecords(IEnumerable<string> labels)
		{
			var list = new List<string>(labels);
			for (int i = 0; i < list.Count; i++)
			{
				PlayerPrefs.DeleteKey(_doneKeyPrefix + list[i]);
			}
			PlayerPrefs.Save();
		}

		// ── 내부 구현 ─────────────────────────────────────────────────────

		// 성공 시 true, 실패(재시도 소진) 시 false 반환, 취소 시 OCE 전파
		private async UniTask<bool> downloadLabelAsync(
			string label,
			LabelProgressReporter reporter,
			int maxRetry,
			CancellationToken ct)
		{
			for (int attempt = 0; attempt <= maxRetry; attempt++)
			{
				(bool cancelled, bool ok) = await AddressableHelper.TryDownloadDependenciesAsync(label, reporter, ct).SuppressCancellationThrow();
				if (cancelled)
				{
					ct.ThrowIfCancellationRequested();
					return false;
				}

				if (ok) { return true; }

				if (attempt >= maxRetry)
				{
					Debug.LogError($"[AssetBundleLoader] '{label}' 다운로드 최대 재시도 초과 ({maxRetry + 1}회)");
					return false;
				}

				int delayMs = _baseRetryDelayMs * (1 << attempt);
				Debug.LogWarning($"[AssetBundleLoader] '{label}' 다운로드 실패 (시도 {attempt + 1}/{maxRetry + 1}), {delayMs}ms 후 재시도.");
				bool delayCancelled = await UniTask.Delay(delayMs, cancellationToken: ct).SuppressCancellationThrow();
				if (delayCancelled)
				{
					ct.ThrowIfCancellationRequested();
					return false;
				}
			}

			return false;
		}

		// 원격 URL의 호스트+스킴을 _serverUrl로 치환
		private string transformInternalId(IResourceLocation location)
		{
			string id = location.InternalId;
			if (!id.StartsWith("http://") && !id.StartsWith("https://")) { return id; }

			int schemeEnd = id.IndexOf("://", StringComparison.Ordinal);
			int pathStart = id.IndexOf('/', schemeEnd + 3);
			if (pathStart < 0) { return id; }

			return _serverUrl.TrimEnd('/') + id.Substring(pathStart);
		}

		private bool isLabelDone(string label)
		{
			return PlayerPrefs.GetInt(_doneKeyPrefix + label, 0) == 1;
		}

		private void markLabelDone(string label)
		{
			PlayerPrefs.SetInt(_doneKeyPrefix + label, 1);
			PlayerPrefs.Save();
		}

		// Addressables IProgress<float>(0~1)를 전체 기준 BundleDownloadProgress로 변환
		private sealed class LabelProgressReporter : IProgress<float>
		{
			private readonly AssetBundleLoader _loader;
			private readonly long _labelSize;
			private readonly long _previousBytes;
			private readonly long _totalBytes;
			private readonly IProgress<BundleDownloadProgress> _outerProgress;

			public LabelProgressReporter(
				AssetBundleLoader loader,
				long labelSize,
				long previousBytes,
				long totalBytes,
				IProgress<BundleDownloadProgress> outerProgress)
			{
				_loader = loader;
				_labelSize = labelSize;
				_previousBytes = previousBytes;
				_totalBytes = totalBytes;
				_outerProgress = outerProgress;
			}

			public void Report(float ratio)
			{
				if (_outerProgress == null) { return; }

				long currentLabelBytes = (long)(ratio * _labelSize);
				long totalDownloaded = _previousBytes + currentLabelBytes;

				// 0.5초 간격으로 속도 갱신
				float now = Time.realtimeSinceStartup;
				float dt = now - _loader._lastSpeedSampleTime;
				if (dt >= 0.5f)
				{
					_loader._lastSpeed = (totalDownloaded - _loader._lastSpeedSampleBytes) / dt;
					_loader._lastSpeedSampleTime = now;
					_loader._lastSpeedSampleBytes = totalDownloaded;
				}

				_outerProgress.Report(new BundleDownloadProgress(totalDownloaded, _totalBytes, _loader._lastSpeed));
			}
		}
	}
}
