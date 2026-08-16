using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Loading;
using ProjectOne.Resources;
using UnityEngine;

namespace ProjectOne.Flow
{
	// 패치 상태 — Title 씬 위 오버레이(별도 씬 없음).
	// 1. 서버 버전체크 (스텁 — 서버 붙으면 checkVersionAsync 본문만 채움)
	// 2. 원격 카탈로그 갱신
	// 3. 번들 다운로드 크기 체크 + 여유 공간 확인
	// 4. 필요 시 번들 다운로드 (진행률 콜백)
	// 5. LoginState로 전이
	//
	// 다운로드 대상 label 목록은 AssetBundleLoader가 보유한 PatchConfig에서 읽는다.
	// PatchConfig는 인스펙터 주입 — AddressableAutoMarker.MarkAll()이 label 자동 갱신.
	public class PatchState : IGameState
	{
		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 로딩 화면 표시 — 여기부터 로비 진입까지 여러 State 를 가로질러 유지된다(ToLobby 흐름).
			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToTown, ct);

			PatchConfig config = AssetBundleLoader.Instance.Config;
			if (config == null || config.DownloadLabels.Count == 0)
			{
				Debug.LogWarning("[PatchState] PatchConfig 미연결 또는 label 없음. 패치 건너뜀. (부트 씬 AssetBundleLoader에 PatchConfig 연결 필요)");
				LoadingManager.Instance.SetPhaseProgress(LoadingPhase.Patch, 1f);
				GameFlow.Instance.ChangeStateAsync(new LoginState()).Forget();
				return;
			}

			// 1. 서버 버전체크 (스텁)
			VersionCheckResult ver = await checkVersionAsync(config, ct);
			if (ver.needStoreUpdate)
			{
				Debug.LogError("[PatchState] 스토어 강제 업데이트 필요.");
				// TODO: 스토어 이동 유도 UI
				return;
			}

			if (ver.underMaintenance)
			{
				Debug.LogError("[PatchState] 서버 점검 중.");
				// TODO: 점검 안내 UI
				return;
			}

			// 서버가 번들 URL을 내려줬으면 런타임 주입 (스텁 단계에선 보통 빈 값)
			if (!string.IsNullOrEmpty(ver.bundleServerUrl))
			{
				AssetBundleLoader.Instance.SetServerUrl(ver.bundleServerUrl);
			}

			var labels = new List<string>(config.DownloadLabels);

			// 2. 카탈로그 갱신
			IReadOnlyList<string> catalogIds = await AssetBundleLoader.Instance.CheckCatalogUpdateAsync(ct);
			if (catalogIds.Count > 0)
			{
				Debug.Log($"[PatchState] 카탈로그 {catalogIds.Count}개 갱신 중...");
				await AssetBundleLoader.Instance.UpdateCatalogsAsync(catalogIds, ct);
			}

			// 3. 다운로드 크기 체크
			BundleCheckResult check = await AssetBundleLoader.Instance.CheckUpdateAsync(labels, ct);
			if (!check.hasUpdate)
			{
				Debug.Log("[PatchState] 다운로드할 번들 없음. 패치 완료.");
				LoadingManager.Instance.SetPhaseProgress(LoadingPhase.Patch, 1f);
				GameFlow.Instance.ChangeStateAsync(new LoginState()).Forget();
				return;
			}

			// 4. 여유 공간 확인
			long available = AssetBundleLoader.Instance.GetAvailableStorageBytes();
			if (available < check.totalBytes)
			{
				long needMB = check.totalBytes / 1024 / 1024;
				long freeMB = available / 1024 / 1024;
				Debug.LogError($"[PatchState] 저장 공간 부족: 필요 {needMB} MB, 여유 {freeMB} MB");
				// TODO: 사용자에게 오류 UI 표시 후 재시도 유도
				return;
			}

			// 5. 다운로드
			long totalMB = check.totalBytes / 1024 / 1024;
			Debug.Log($"[PatchState] 다운로드 시작: {totalMB} MB ({labels.Count}개 그룹)");
			var progressReporter = new System.Progress<BundleDownloadProgress>(onPatchProgress);
			await AssetBundleLoader.Instance.DownloadAsync(labels, progressReporter, maxRetry: 3, ct);

			Debug.Log("[PatchState] 다운로드 완료.");
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.Patch, 1f);
			GameFlow.Instance.ChangeStateAsync(new LoginState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		private void onPatchProgress(BundleDownloadProgress prog)
		{
			// 패치 단계 로컬 비율 보고 (System.Progress 콜백이라 메인스레드로 마샬됨 → UI 갱신 안전)
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.Patch, prog.ratio);

			float pct = prog.ratio * 100f;
			long dlMB = prog.downloadedBytes / 1024 / 1024;
			long totalMB = prog.totalBytes / 1024 / 1024;
			float kbps = prog.speedBytesPerSec / 1024f;
			Debug.Log($"[PatchState] {pct:F1}% ({dlMB}/{totalMB} MB) {kbps:F0} KB/s");
		}

		// ── 서버 버전체크 (골대 — 서버 준비 후 본문만 채움) ──────────────

		private readonly struct VersionCheckResult
		{
			public readonly bool needStoreUpdate;   // 스토어 강제 업데이트
			public readonly bool underMaintenance;  // 서버 점검 중
			public readonly string bundleServerUrl; // 번들/카탈로그 CDN URL (없으면 빈 문자열)

			public VersionCheckResult(bool store, bool maint, string url)
			{
				needStoreUpdate = store;
				underMaintenance = maint;
				bundleServerUrl = url;
			}
		}

		private async UniTask<VersionCheckResult> checkVersionAsync(PatchConfig config, CancellationToken ct)
		{
			// TODO: config.VersionCheckUrl 로 서버 호출 → 응답(JSON) 파싱
			//  - 앱버전 비교 → needStoreUpdate
			//  - 점검 플래그 → underMaintenance
			//  - 응답의 CDN URL → bundleServerUrl
			await UniTask.CompletedTask;  // 스텁 — 서버 미연결
			return new VersionCheckResult(false, false, config.DefaultServerUrl);
		}
	}
}
