using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Audio;

namespace ProjectOne.Flow
{
	// 부트 상태 — 정적 테이블/SFX 로드 등 초기화.
	// 기존 GameBootstrapper.bootAsync 시퀀스를 그대로 옮겨왔다.
	// 1.Bootstrap 씬은 이미 로드된 상태이므로 씬 로드는 하지 않는다.
	public class BootState : IGameState
	{
		public async UniTask EnterAsync(CancellationToken ct)
		{
			try
			{
				// 1) 정적 테이블 일괄 로드 (Addressables 라벨 "Tables")
				await TableLoader.LoadAllAsync(ct);

				// 2) SFX 클립 일괄 프리로드 (Addressables 라벨 "SFX") — 첫 재생 끊김 방지
				await AudioManager.Instance.PreloadSFXByLabelAsync("SFX", ct);

				Debug.Log("부트 완료 — Character:" + Table_Character.All().Count
					+ " Monster:" + Table_Monster.All().Count
					+ " BaseStat:" + Table_BaseStat.All().Count);
			}
			catch (OperationCanceledException)
			{
				// 정상 취소 — 무시
				return;
			}
			catch (Exception e)
			{
				Debug.LogError("부트 실패: " + e.Message);
				return;
			}

			// 다음 상태로 자동 전이
			GameFlow.Instance.ChangeStateAsync(new TitleState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
