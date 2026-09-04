using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 경험치 던전 — 무한형. 제한시간 동안 스폰 그룹을 반복 소환하고, 시간이 다하면 클리어한다
	// (몬스터 설계 8장: "Exp(무한형) 배열에서 뽑아 슬롯에 반복 스폰").
	//
	// 전멸이 클리어 조건이 아니므로 계속 채워 넣는다. 많이 잡을수록 경험치를 많이 얻는 구조다.
	public sealed class ExpDungeonMode : StageModeBase
	{
		// 제한시간(초) — 밸런싱 대상이 아니라 모드의 성격이므로 코드 상수다.
		private const float DurationSeconds = 60f;

		// 재소환 주기(초)
		private const float RespawnInterval = 3f;

		private CancellationTokenSource _spawnCts;

		protected override async UniTask RunAsync(Table_DungeonStage.Row stage, CancellationToken ct)
		{
			int[] groups = GetSpawnGroups(stage);
			int levelOverride = GetLevelOverride(stage);

			if (groups.Length <= 0)
			{
				Debug.LogWarning($"[ExpDungeonMode] MonsterSpawnGroupIDs 가 비어 있음 — 몬스터가 나오지 않습니다. DungeonStage:{stage.ID}");
				return;
			}

			_spawnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			// 무한형이라 웨이브 개념이 없다 — 배열을 순환하며 계속 채워 넣는다 (몬스터 설계 8장).
			int groupCursor = 0;

			float elapsed = 0f;
			float sinceSpawn = RespawnInterval;	// 시작 즉시 1회 소환
			while (elapsed < DurationSeconds)
			{
				if (sinceSpawn >= RespawnInterval)
				{
					sinceSpawn = 0f;
					SpawnGroupRunner.SpawnGroup(groups[groupCursor % groups.Length], levelOverride);
					groupCursor++;
				}

				await UniTask.Yield(PlayerLoopTiming.Update, ct);
				elapsed += Time.deltaTime;
				sinceSpawn += Time.deltaTime;
			}

			// 시간 종료 — 남은 몬스터를 정리하고 클리어 처리
			_spawnCts.Cancel();
			MonsterSpawnManager.Instance.ClearAlive();

			_result = DungeonResult.Cleared;
		}

		protected override void OnFinished()
		{
			if (_spawnCts != null)
			{
				_spawnCts.Dispose();
				_spawnCts = null;
			}
		}
	}
}
