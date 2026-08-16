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
	// 골드 던전 — 웨이브형. 스폰 그룹을 소환하고 전멸시키면 클리어한다 (몬스터 설계 8장).
	//
	// 설계상 `DungeonStage.MonsterSpawnGroupIDs` 는 배열이고 "배열 순서 = 웨이브 순서"지만,
	// 컨버터가 단수 int 로 생성해 현재는 1웨이브다. 배열이 되면 이 루프가 그대로 여러 웨이브를 돈다.
	public sealed class GoldDungeonMode : StageModeBase
	{
		// 웨이브 간 대기 시간(초) — 대기 중 메인HUD에 스킵 버튼이 노출된다.
		private const float WaveWaitSeconds = 30f;

		private bool _skipRequested;
		private Action<WaveSkipRequestedEvent> _onSkipRequested;

		private CancellationTokenSource _spawnCts;

		protected override async UniTask RunAsync(Table_DungeonStage.Row stage, CancellationToken ct)
		{
			_onSkipRequested = onSkipRequested;
			EventManager.Instance.Subscribe<WaveSkipRequestedEvent>(_onSkipRequested);

			int groupId = GetSpawnGroupId(stage);
			int levelOverride = GetLevelOverride(stage);

			// 스폰 그룹이 비어 있으면 전멸 판정이 즉시 참이 되어 그냥 클리어된다.
			// 데이터 누락이 조용히 넘어가지 않도록 알린다.
			if (groupId <= 0)
			{
				Debug.LogWarning($"[GoldDungeonMode] MonsterSpawnGroupIDs 가 비어 있음 — 몬스터 없이 즉시 클리어됩니다. DungeonStage:{stage.ID}");
			}

			const int totalWaves = 1;
			for (int wave = 1; wave <= totalWaves; wave++)
			{
				EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, false, 0f, false));

				_spawnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				SpawnGroupRunner.SpawnGroup(groupId, levelOverride, _spawnCts.Token);

				// 이 웨이브 몬스터 전멸까지 대기
				await UniTask.WaitUntil(AreMonstersCleared, PlayerLoopTiming.Update, ct);

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
					EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, true, 0f, false));
				}
			}

			_result = DungeonResult.Cleared;
		}

		private async UniTask waitBetweenWavesAsync(int wave, int totalWaves, CancellationToken ct)
		{
			_skipRequested = false;
			EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, true, WaveWaitSeconds, true));

			// 시간 경과 자동 전환은 미사용 — 스킵(버튼/스페이스바)으로만 다음 웨이브로 진행한다.
			while (_skipRequested == false)
			{
				if (isSkipKeyPressed() == true)
				{
					_skipRequested = true;
					break;
				}

				await UniTask.Yield(PlayerLoopTiming.Update, ct);
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
