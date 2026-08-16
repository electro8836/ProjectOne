using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// Table_MonsterSpawn(GroupID 묶음)을 실제 소환으로 구동한다.
	//
	// 신규 스키마는 `GroupID / MonsterID / Level / Count` 뿐이다.
	// 구버전의 `SpawnPoint`(위치) · `SpawnStepCount`/`SpawnStepDelay`(지속 소환) 컬럼이 사라졌다.
	//
	// **위치는 씬(맵 프리팹)의 던전 슬롯이 소유한다** (몬스터 설계 7장 — 익명 슬롯).
	// 슬롯 컴포넌트는 STEP 8에서 만들고, 그때까지는 히어로 주변에 배치한다.
	internal static class SpawnGroupRunner
	{
		// 슬롯이 없을 때 히어로 주변 배치 반경
		private const float FallbackSpawnRadius = 4f;

		// 같은 지점에 겹치지 않도록 개체마다 흩뿌리는 반경
		private const float ScatterRadius = 1f;

		// GroupID 필터링용 재사용 버퍼 (Dictionary 전체 순회 회피)
		private static readonly List<Table_MonsterSpawn.Row> _rowBuffer = new List<Table_MonsterSpawn.Row>();

		// 그룹의 모든 행을 즉시 소환한다.
		// levelOverride > 0 이면 MonsterSpawn.Level 대신 그 값을 쓴다 (DungeonStage.MonsterLevel).
		public static void SpawnGroup(int groupId, int levelOverride, CancellationToken ct)
		{
			if (groupId <= 0)
			{
				return;
			}

			_rowBuffer.Clear();
			_rowBuffer.AddRange(Table_MonsterSpawn.All().Values);

			Vector3 basePos = resolveBasePos();
			bool matched = false;

			for (int i = 0; i < _rowBuffer.Count; i++)
			{
				Table_MonsterSpawn.Row row = _rowBuffer[i];
				if (row.GroupID != groupId || row.MonsterID <= 0)
				{
					continue;
				}

				matched = true;
				int level = (levelOverride > 0) ? levelOverride : row.Level;
				if (level <= 0)
				{
					level = 1;
				}

				int count = (row.Count > 0) ? row.Count : 1;
				for (int n = 0; n < count; n++)
				{
					MonsterSpawnManager.Instance.SpawnOneShot(row.MonsterID, level, randomAround(basePos));
				}
			}

			if (matched == false)
			{
				Debug.LogWarning($"[SpawnGroupRunner] GroupID {groupId} 에 해당하는 MonsterSpawn 행이 없습니다.");
			}
		}

		// TODO(STEP 8) — 맵의 던전 슬롯(SlotIndex)을 찾아 그 위치를 쓴다.
		// 지금은 히어로 앞쪽에 배치한다.
		private static Vector3 resolveBasePos()
		{
			if (UnitContainer.HasInstance == false)
			{
				return Vector3.zero;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null)
				{
					Vector2 offset = Random.insideUnitCircle.normalized * FallbackSpawnRadius;
					return hero.transform.position + new Vector3(offset.x, offset.y, 0f);
				}
			}

			return Vector3.zero;
		}

		private static Vector3 randomAround(Vector3 center)
		{
			Vector2 val = Random.insideUnitCircle * ScatterRadius;
			return center + new Vector3(val.x, val.y, 0f);
		}
	}
}
