using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 방어형 모드 — ClearValue 만큼의 웨이브를 순차 진행한다.
	// 웨이브 N → MonsterSpawnGroupIDs[N-1] 그룹 소환 → 전멸 시 (마지막이 아니면) 대기 후 다음 웨이브.
	// 마지막 웨이브 전멸 시 클리어.
	public sealed class DefenseStageMode : StageModeBase
	{
		// 웨이브 간 대기 시간(초) — 고정. 대기 중 메인HUD에 스킵 버튼이 노출된다.
		private const float WaveWaitSeconds = 30f;

		private bool _skipRequested;
		private Action<WaveSkipRequestedEvent> _onSkipRequested;

		// 현재 웨이브의 지속 소환(SpawnStep) 중단용 — 전멸 시 취소
		private CancellationTokenSource _spawnCts;

		protected override async UniTask RunAsync(Table_Stage.Row stage, CancellationToken ct)
		{
			_onSkipRequested = onSkipRequested;
			EventManager.Instance.Subscribe<WaveSkipRequestedEvent>(_onSkipRequested);

			int totalWaves = Mathf.Max(1, stage.ClearValue);
			for (int wave = 1; wave <= totalWaves; wave++)
			{
				int groupId = GetGroupId(stage.MonsterSpawnGroupIDs, wave - 1);
				EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, false, 0f, false));

				// 웨이브 전용 소환 CTS — 전멸 시 취소해 지속(SpawnStep) 소환을 멈춘다
				_spawnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				SpawnGroupRunner.SpawnGroup(groupId, _spawnCts.Token);

				// 이 웨이브 몬스터 전멸까지 대기
				await UniTask.WaitUntil(AreMonstersCleared, PlayerLoopTiming.Update, ct);

				// 지속 소환 중단 + 잔존 몬스터 즉시 제거(직전에 막 소환된 개체 대비)
				_spawnCts.Cancel();
				_spawnCts.Dispose();
				_spawnCts = null;
				MonsterSpawnManager.Instance.ClearAlive();

				if (wave < totalWaves)
				{
					await waitBetweenWavesAsync(wave, totalWaves, ct);
				}
				else
				{
					// 마지막 웨이브 클리어 — "M/M단계 방어 완료" 배너 표시(스킵 버튼 없음)
					EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, true, 0f, false));
				}
			}

			// 스테이지 종료 상점은 DungeonDirector 가 본편 클리어 시 스폰한다(엑스트라 제외).
			_result = DungeonResult.Cleared;
		}

		private async UniTask waitBetweenWavesAsync(int wave, int totalWaves, CancellationToken ct)
		{
			_skipRequested = false;
			EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, true, WaveWaitSeconds, true));

			// 웨이브 간 대기 동안 히어로 위치에 상점 기믹 등장 (다음 웨이브 시작 시 회수)
			GimmickService.Instance.SpawnAtHeroAsync(GimmickService.ShopGimmickAddress).Forget();

			// 시간 경과 자동 전환은 미사용 — 스킵(버튼/스페이스바)으로만 다음 웨이브로 진행한다.
			// elapsed 누적과 WaveWaitSeconds 비교 로직은 추후 재사용을 위해 보존하되 종료 조건에서는 뺀다.
			float elapsed = 0f;
			while (_skipRequested == false)
			{
				if (isSkipKeyPressed() == true)
				{
					_skipRequested = true;
					break;
				}

				await UniTask.Yield(PlayerLoopTiming.Update, ct);
				elapsed += Time.deltaTime;
			}

			if (GimmickService.HasInstance == true)
			{
				GimmickService.Instance.DespawnAll();
			}
		}

		private void onSkipRequested(WaveSkipRequestedEvent evt)
		{
			_skipRequested = true;
		}

		// 스페이스바 스킵 — 새 Input System 기준
		private static bool isSkipKeyPressed()
		{
			Keyboard kb = Keyboard.current;
			return kb != null && kb.spaceKey.wasPressedThisFrame;
		}

		protected override void OnFinished()
		{
			if (_onSkipRequested != null)
			{
				EventManager.Instance.Unsubscribe<WaveSkipRequestedEvent>(_onSkipRequested);
				_onSkipRequested = null;
			}

			if (_spawnCts != null)
			{
				_spawnCts.Dispose();
				_spawnCts = null;
			}
		}
	}
}
