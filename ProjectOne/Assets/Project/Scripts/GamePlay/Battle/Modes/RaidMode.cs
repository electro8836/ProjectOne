using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;

namespace ProjectOne.Battle
{
	// 레이드형 모드 — 보통 SpawnCount=1, SpawnInfo_01 에 보스 1마리를 설정한다(쌍둥이 보스 등은 한 행에 다중 테이블).
	// 보스를 모두 처치하면 승리. 웨이브 대기 없음.
	public sealed class RaidMode : MapBattleModeBase
	{
		protected override async UniTaskVoid RunAsync(Table_MapInfo.Row map, CancellationToken ct)
		{
			Table_MonsterSpawnInfo.Row spawnInfo = Table_MonsterSpawnInfo.Get(map.SpawnInfo_01);
			if (spawnInfo == null)
			{
				Debug.LogError($"[RaidMode] SpawnInfo 없음: id={map.SpawnInfo_01}");
				return;
			}

			await SpawnInfoRunner.SpawnAsync(spawnInfo, MonsterSpawnCenter, MonsterSpawnRadius, ct);
			await UniTask.WaitUntil(AreMonstersCleared, PlayerLoopTiming.Update, ct);

			_result = BattleResult.Victory;
		}
	}
}
