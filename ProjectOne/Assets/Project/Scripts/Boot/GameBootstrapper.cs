using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Audio;

namespace ProjectOne.Boot
{
	// 게임 진입점.
	// - 부트 씬에 단 한 번 배치되어 Start에서 부트 시퀀스를 비동기로 실행
	// - 정적 테이블 로드를 비롯한 초기화가 끝난 뒤에야 다른 매니저/씬으로 진입
	public class GameBootstrapper : MonoBehaviour
	{
		private readonly CancellationTokenSource _cts = new CancellationTokenSource();

		private async UniTaskVoid Start()
		{
			try
			{
				await bootAsync(_cts.Token);
			}
			catch (OperationCanceledException)
			{
				// 정상 취소 — 무시
			}
			catch (Exception e)
			{
				Debug.LogError("부트 실패: " + e.Message);
			}
		}

		private async UniTask bootAsync(CancellationToken ct)
		{
			// 1) 정적 테이블 일괄 로드 (Addressables 라벨 "Tables")
			await TableLoader.LoadAllAsync(ct);

			// 2) SFX 클립 일괄 프리로드 (Addressables 라벨 "SFX") — 첫 재생 끊김 방지
			await AudioManager.Instance.PreloadSFXByLabelAsync("SFX", ct);

			Debug.Log("부트 완료 — Character:" + Table_Character.All().Count
				+ " Monster:" + Table_Monster.All().Count
				+ " BaseStat:" + Table_BaseStat.All().Count);
		}

		private void OnDestroy()
		{
			_cts.Cancel();
			_cts.Dispose();
		}
	}
}
