using System;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Unit
{
	[Serializable]
	public class SpawnData
	{
		[Tooltip("스폰할 몬스터의 Table_Monster ID")]
		public int monsterId;

		[Tooltip("스폰 영역 중심 (월드 좌표)")]
		public Vector3 center;

		[Tooltip("중심으로부터 스폰 반경 — 이 반경 내 랜덤 위치에 생성 (0이면 중심 고정)")]
		public float radius = 3f;

		[Tooltip("최소 유지 수 — 현재 수가 이보다 적으면 즉시 충원")]
		public int minCount = 1;

		[Tooltip("최대 수 — 이 수까지만 스폰 주기마다 부족분 충원")]
		public int maxCount = 5;

		[Tooltip("스폰 주기(초) — 최소~최대 구간에서 이 간격마다 부족분 충원")]
		public float spawnInterval = 2f;

		[NonSerialized]
		public int aliveCount;

		[NonSerialized]
		public IntervalTimer spawnTimer;

		public Vector3 RandomPosition()
		{
			Vector2 val = UnityEngine.Random.insideUnitCircle * radius;
			return center + new Vector3(val.x, val.y, 0f);
		}
	}
}
