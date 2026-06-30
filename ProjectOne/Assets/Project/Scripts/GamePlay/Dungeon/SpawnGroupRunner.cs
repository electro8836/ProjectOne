using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Map;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 신규 Table_MonsterSpawn(GroupID 묶음)을 실제 소환으로 구동한다.
	// - SpawnPoint: 맵 프리팹 하위 게임오브젝트 이름 → 그 위치 반경 SpawnRadius 안에 랜덤 배치(없으면 원점 폴백)
	// - SpawnAllCount: 즉시 소환
	// - SpawnStepCount / SpawnStepDelay: ct 취소(=클리어) 전까지 SpawnStepDelay 마다 SpawnStepCount 만큼 지속 소환
	internal static class SpawnGroupRunner
	{
		// 스폰포인트 기준 랜덤 배치 반경 (임시 — 추후 SpawnPoint 클래스로 대체)
		private const float SpawnRadius = 1f;

		// GroupID 필터링용 임시 버퍼 (Dictionary 전체 순회 회피용 재사용)
		private static readonly List<Table_MonsterSpawn.Row> _rowBuffer = new List<Table_MonsterSpawn.Row>();

		// 그룹의 모든 행을 소환한다. 즉시분(SpawnAllCount)은 호출 즉시 소환하고,
		// 지속분(SpawnStep)은 백그라운드로 ct 취소 시까지 계속 소환한다(호출자는 클리어 시 ct 를 취소).
		public static void SpawnGroup(int groupId, CancellationToken ct)
		{
			if (groupId <= 0)
			{
				return;
			}

			_rowBuffer.Clear();
			_rowBuffer.AddRange(Table_MonsterSpawn.All().Values);

			for (int i = 0; i < _rowBuffer.Count; i++)
			{
				Table_MonsterSpawn.Row row = _rowBuffer[i];
				if (row.GroupID != groupId || row.MonsterID <= 0)
				{
					continue;
				}

				Vector3 basePos = resolveSpawnPos(row.SpawnPoint);

				// 즉시 소환
				for (int n = 0; n < row.SpawnAllCount; n++)
				{
					MonsterSpawnManager.Instance.SpawnOneShot(row.MonsterID, randomAround(basePos));
				}

				// 지속 소환 (클리어 전까지 반복)
				if (row.SpawnStepCount > 0 && row.SpawnStepDelay > 0f)
				{
					spawnStepLoopAsync(row.MonsterID, row.SpawnStepCount, row.SpawnStepDelay, basePos, ct).Forget();
				}
			}
		}

		// SpawnStepDelay 마다 SpawnStepCount 만큼 소환. ct 취소 시 종료(미관측 예외 방지: SuppressCancellationThrow).
		private static async UniTaskVoid spawnStepLoopAsync(int monsterId, int stepCount, float stepDelay, Vector3 basePos, CancellationToken ct)
		{
			while (true)
			{
				bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(stepDelay), ignoreTimeScale: false, PlayerLoopTiming.Update, ct).SuppressCancellationThrow();
				if (canceled == true)
				{
					return;
				}

				for (int n = 0; n < stepCount; n++)
				{
					MonsterSpawnManager.Instance.SpawnOneShot(monsterId, randomAround(basePos));
				}
			}
		}

		// 스폰포인트 이름을 맵에서 해석. 없으면 경고 후 원점 폴백.
		private static Vector3 resolveSpawnPos(string spawnPoint)
		{
			if (MapManager.HasInstance == true)
			{
				Transform t = MapManager.Instance.GetSpawnPoint(spawnPoint);
				if (t != null)
				{
					return t.position;
				}
			}

			Debug.LogWarning($"[SpawnGroupRunner] 스폰포인트 '{spawnPoint}' 를 맵에서 찾지 못함 → 원점 폴백");
			return Vector3.zero;
		}

		private static Vector3 randomAround(Vector3 center)
		{
			Vector2 val = UnityEngine.Random.insideUnitCircle * SpawnRadius;
			return center + new Vector3(val.x, val.y, 0f);
		}
	}
}
