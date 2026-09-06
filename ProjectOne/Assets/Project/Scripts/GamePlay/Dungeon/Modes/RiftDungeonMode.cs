using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 균열 던전 — 페이즈별 처치 목표형.
	//
	// 페이즈 수는 DungeonStage.MonsterSpawnGroupIDs 길이가 정한다(그룹 3개면 3페이즈).
	// 총 처치 목표·화면 유지 인원·소환 주기는 난이도와 무관한 고정 규칙이라 테이블이 아니라 여기 상수다.
	//
	// 웨이브형(GoldDungeonMode)과 달리 전멸이 페이즈 종료 조건이 아니다. 화면 인원을 계속 채워 넣다가
	// **이번 페이즈에서 소환할 몫을 다 쓰면 소환을 멈추고**, 남은 몬스터를 마저 잡아 목표 킬이 차면 넘어간다.
	// (예: 목표 100 중 이미 100마리를 소환했고 70킬이면, 화면의 30마리를 다 잡는 순간 페이즈 종료)
	public sealed class RiftDungeonMode : StageModeBase
	{
		// 던전 전체 처치 목표. 페이즈 수로 나눠 페이즈별 목표가 된다.
		private const int TotalKills = 300;

		// 화면 유지 하한 — 이 밑으로 떨어지면 주기를 기다리지 않고 즉시 채운다.
		private const int MinAlive = 20;

		// 화면 유지 상한
		private const int MaxAlive = 50;

		// 정기 소환 주기와 1회 소환 마리 수
		private const float SpawnInterval = 2f;
		private const int SpawnBatch = 2;

		// 페이즈 배너 연출이 걷힐 때까지 소환을 미루는 고정 지연(초)
		private const float PhaseStartDelay = 3f;

		// 이번 페이즈의 처치 수. 리스폰형이라 ActiveCount 뺄셈으로는 셀 수 없어 처치 이벤트로 센다.
		private int _phaseKilled;

		private Action<MonsterKillEvent> _onMonsterKill;

		protected override async UniTask RunAsync(Table_DungeonStage.Row stage, CancellationToken ct)
		{
			int[] groups = GetSpawnGroups(stage);
			int phaseCount = groups.Length;

			if (phaseCount <= 0)
			{
				Debug.LogError($"[RiftDungeonMode] MonsterSpawnGroupIDs 가 비어 있습니다 — DungeonStage:{stage.ID}");
				return;
			}

			int levelOverride = GetLevelOverride(stage);

			_onMonsterKill = onMonsterKill;
			EventManager.Instance.Subscribe<MonsterKillEvent>(_onMonsterKill);

			for (int phase = 0; phase < phaseCount; phase++)
			{
				int phaseTarget = getPhaseTarget(phase, phaseCount);

				EventManager.Instance.Publish(new WaveStartedEvent(phase + 1, phaseCount, phaseTarget));

				await UniTask.Delay(TimeSpan.FromSeconds(PhaseStartDelay), DelayType.DeltaTime, PlayerLoopTiming.Update, ct);

				await runPhaseAsync(groups[phase], levelOverride, phaseTarget, ct);
			}

			_result = DungeonResult.Cleared;
		}

		protected override void OnFinished()
		{
			if (_onMonsterKill == null)
			{
				return;
			}

			EventManager.Instance.Unsubscribe<MonsterKillEvent>(_onMonsterKill);
			_onMonsterKill = null;
		}

		// ── 페이즈 ────────────────────────────────────────────────────

		private async UniTask runPhaseAsync(int groupId, int levelOverride, int phaseTarget, CancellationToken ct)
		{
			_phaseKilled = 0;

			// 이번 페이즈에서 이미 소환한 마리 수. 목표만큼 다 소환하면 더 내보내지 않는다.
			int spawned = 0;
			float sinceSpawn = SpawnInterval;

			while (_phaseKilled < phaseTarget)
			{
				int budget = phaseTarget - spawned;
				if (budget > 0)
				{
					int alive = MonsterSpawnManager.Instance.ActiveCount;
					int want = getSpawnCount(alive, sinceSpawn);
					if (want > budget)
					{
						want = budget;
					}

					if (want > 0)
					{
						SpawnGroupRunner.SpawnFromGroup(groupId, levelOverride, want);
						spawned += want;
						sinceSpawn = 0f;
					}
				}

				await UniTask.Yield(PlayerLoopTiming.Update, ct);
				sinceSpawn += Time.deltaTime;
			}
		}

		// 이번 프레임에 내보낼 마리 수. 하한을 깨면 주기를 무시하고 즉시 채운다.
		private static int getSpawnCount(int alive, float sinceSpawn)
		{
			if (alive < MinAlive)
			{
				return MinAlive - alive;
			}

			if (sinceSpawn < SpawnInterval || alive >= MaxAlive)
			{
				return 0;
			}

			int room = MaxAlive - alive;
			return (SpawnBatch < room) ? SpawnBatch : room;
		}

		// 나누어떨어지지 않는 나머지는 마지막 페이즈가 떠안는다 — 총합이 TotalKills 와 어긋나지 않도록.
		private static int getPhaseTarget(int phase, int phaseCount)
		{
			int perPhase = TotalKills / phaseCount;
			if (phase < phaseCount - 1)
			{
				return perPhase;
			}

			return TotalKills - perPhase * (phaseCount - 1);
		}

		private void onMonsterKill(MonsterKillEvent evt)
		{
			_phaseKilled++;
		}
	}
}
