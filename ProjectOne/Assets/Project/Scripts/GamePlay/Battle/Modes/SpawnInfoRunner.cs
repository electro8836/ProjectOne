using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Battle
{
	// Table_MonsterSpawnInfo 한 행(최대 3테이블)을 실제 소환으로 구동한다. 웨이브/레이드 공용.
	// 각 테이블: 총량(MonsterCount)을 회당 수(IntervalCount)씩 간격(IntervalTime)마다 나눠 소환.
	// 모든 테이블을 병렬로 진행하며, 전부 "소환 완료"되면 반환한다(처치 대기는 호출부 책임).
	internal static class SpawnInfoRunner
	{
		public static async UniTask SpawnAsync(Table_MonsterSpawnInfo.Row row, Vector3 center, float radius, CancellationToken ct)
		{
			if (row == null)
			{
				return;
			}

			UniTask t1 = SpawnTableAsync(row.MonsterID_01, row.MonsterCount_01, row.IntervalCount_01, row.IntervalTime_01, center, radius, ct);
			UniTask t2 = SpawnTableAsync(row.MonsterID_02, row.MonsterCount_02, row.IntervalCount_02, row.IntervalTime_02, center, radius, ct);
			UniTask t3 = SpawnTableAsync(row.MonsterID_03, row.MonsterCount_03, row.IntervalCount_03, row.IntervalTime_03, center, radius, ct);
			await UniTask.WhenAll(t1, t2, t3);
		}

		private static async UniTask SpawnTableAsync(int monsterId, int total, int batch, float interval, Vector3 center, float radius, CancellationToken ct)
		{
			if (monsterId <= 0 || total <= 0)
			{
				return;
			}

			if (batch <= 0)
			{
				batch = total;
			}

			int spawned = 0;
			while (spawned < total)
			{
				int n = Mathf.Min(batch, total - spawned);
				for (int i = 0; i < n; i++)
				{
					Vector3 pos = RandomPosition(center, radius);
					MonsterSpawnManager.Instance.SpawnOneShot(monsterId, pos);
				}

				spawned += n;

				if (spawned < total && interval > 0f)
				{
					await UniTask.Delay(TimeSpan.FromSeconds(interval), ignoreTimeScale: false, PlayerLoopTiming.Update, ct);
				}
			}
		}

		private static Vector3 RandomPosition(Vector3 center, float radius)
		{
			Vector2 val = UnityEngine.Random.insideUnitCircle * radius;
			return center + new Vector3(val.x, val.y, 0f);
		}
	}
}
