using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Audio;
using ProjectOne.Data;
using ProjectOne.Resources;
using ProjectOne.Settings;

namespace ProjectOne.Flow
{
	// 부트 상태 — 정적 테이블/SFX 로드 등 초기화.
	// 기존 GameBootstrapper.bootAsync 시퀀스를 그대로 옮겨왔다.
	// 1.Bootstrap 씬은 이미 로드된 상태이므로 씬 로드는 하지 않는다.
	public class BootState : IGameState
	{
		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 0) 로컬 설정 로드 (가벼움, 타이틀 전 적용)
			SettingsManager.Instance.Load();

			// 1) 정적 테이블 일괄 로드 (Addressables 라벨	 "Tables" → 자동생성 EDT.Loader)
			var (cancelled, ok) = await TableBootLoader.LoadAllAsync(ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}
			if (ok == false)
			{
				return;
			}

			// 2) SFX 클립 일괄 프리로드 (Addressables 라벨 "SFX") — 첫 재생 끊김 방지
			cancelled = await AudioManager.Instance.PreloadSFXByLabelAsync("SFX", ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 3) 아웃게임 UI 아이콘 아틀라스 로드 (AtlasManifest 주입 목록) — 화면 열 때 슬롯/아이콘 즉시 표시
			AtlasManifest atlasManifest = AssetBundleLoader.Instance.AtlasManifest;
			if (atlasManifest != null && atlasManifest.AtlasAddresses.Count > 0)
			{
				cancelled = await AtlasManager.Instance.LoadAsync(atlasManifest.AtlasAddresses, ct).SuppressCancellationThrow();
				if (cancelled)
				{
					return;
				}
			}
			else
			{
				Debug.LogWarning("[BootState] AtlasManifest 미연결 또는 비어 있음. 아틀라스 로드 건너뜀. (부트 씬 AssetBundleLoader에 AtlasManifest 연결 + Mark All 실행 필요)");
			}

			Debug.Log("부트 완료 — Character:" + Table_Character.All().Count
				+ " Monster:" + Table_Monster.All().Count
				+ " BaseStat:" + Table_BaseStat.All().Count);

			// 다음 상태로 자동 전이
			GameFlow.Instance.ChangeStateAsync(new TitleState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
