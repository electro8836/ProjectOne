using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Monsters;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 골드 던전 — 웨이브형. `MonsterSpawnGroupIDs` 배열을 앞에서부터 한 그룹씩 소환하고,
	// 그 그룹을 전멸시키면 다음 그룹으로 넘어간다. 더 소환할 그룹이 없으면 클리어다 (몬스터 설계 8장).
	//
	// **배열 순서가 곧 웨이브 순서**이므로 웨이브 수는 코드가 아니라 테이블이 정한다.
	public sealed class GoldDungeonMode : StageModeBase
	{
		// 웨이브 시작 알림 후 실제 스폰까지의 고정 지연(초).
		//
		// UI 배너의 유지·페이드 시간과 **무관한 고정값**이다. 연출 시간을 바꿔도 스폰 시점은 움직이지 않는다.
		private const float WaveStartDelay = 3f;

		protected override async UniTask RunAsync(Table_DungeonStage.Row stage, CancellationToken ct)
		{
			int[] groups = GetSpawnGroups(stage);
			int totalWaves = groups.Length;

			// 웨이브가 없으면 전멸 판정이 즉시 참이 되어 그냥 클리어된다.
			// 데이터 누락이 조용히 넘어가지 않도록 알리고, 클리어로 처리하지 않는다.
			if (totalWaves <= 0)
			{
				Debug.LogError($"[GoldDungeonMode] MonsterSpawnGroupIDs 가 비어 있습니다 — DungeonStage:{stage.ID}");
				return;
			}

			int levelOverride = GetLevelOverride(stage);

			for (int wave = 0; wave < totalWaves; wave++)
			{
				int groupId = groups[wave];
				EventManager.Instance.Publish(new WaveStartedEvent(wave + 1, totalWaves, countGroupMonsters(groupId)));

				await UniTask.Delay(TimeSpan.FromSeconds(WaveStartDelay), DelayType.DeltaTime, PlayerLoopTiming.Update, ct);

				SpawnGroupRunner.SpawnGroup(groupId, levelOverride);

				// 이 웨이브 몬스터 전멸까지 대기.
				// ActiveCount 는 스폰 요청 즉시 오르므로 소환 직후 조기 참이 되지 않는다.
				await UniTask.WaitUntil(AreMonstersCleared, PlayerLoopTiming.Update, ct);

				MonsterSpawnManager.Instance.ClearAlive();
			}

			_result = DungeonResult.Cleared;
		}

		// 이 그룹이 소환하는 총 마리 수 — 진행 게이지의 분모다.
		// SpawnGroupRunner 와 같은 규칙(Count 가 0 이하면 1마리)으로 세야 표시와 실제가 어긋나지 않는다.
		private static int countGroupMonsters(int groupId)
		{
			IReadOnlyList<Table_MonsterSpawn.Row> rows = MonsterCatalog.GetSpawnGroup(groupId);

			int total = 0;
			for (int i = 0; i < rows.Count; i++)
			{
				Table_MonsterSpawn.Row row = rows[i];
				if (row.MonsterID <= 0)
				{
					continue;
				}

				total += (row.Count > 0) ? row.Count : 1;
			}

			return total;
		}
	}
}
