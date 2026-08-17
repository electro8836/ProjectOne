using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// Table_MonsterSpawn(GroupID 묶음)을 실제 소환으로 구동한다.
	//
	// 신규 스키마는 `GroupID / MonsterID / Level / Count` 뿐이다 — 위치 컬럼이 없다.
	// **위치는 씬(맵 프리팹)의 던전 슬롯이 소유한다** (몬스터 설계 7장 — 익명 슬롯).
	// 씬은 "여기가 스폰 자리"만 알려주고 뭐가 나올지는 모른다.
	internal static class SpawnGroupRunner
	{
		// 슬롯이 하나도 없을 때 히어로 주변 배치 반경 (폴백)
		private const float FallbackSpawnRadius = 4f;

		// 같은 지점에 겹치지 않도록 개체마다 흩뿌리는 반경
		private const float ScatterRadius = 1f;

		// 벽 밀어내기용 유닛 반지름 근사
		private const float SpawnClearance = 0.5f;

		// 그룹의 모든 행을 즉시 소환한다.
		// levelOverride > 0 이면 MonsterSpawn.Level 대신 그 값을 쓴다 (DungeonStage.MonsterLevel).
		public static void SpawnGroup(int groupId, int levelOverride)
		{
			if (groupId <= 0)
			{
				return;
			}

			IReadOnlyList<Table_MonsterSpawn.Row> rows = MonsterCatalog.GetSpawnGroup(groupId);
			if (rows.Count == 0)
			{
				Debug.LogWarning($"[SpawnGroupRunner] GroupID {groupId} 에 해당하는 MonsterSpawn 행이 없습니다.");
				return;
			}

			IReadOnlyList<DungeonSpawnSlot> slots = getSlots();
			int slotCursor = 0;

			for (int i = 0; i < rows.Count; i++)
			{
				Table_MonsterSpawn.Row row = rows[i];
				if (row.MonsterID <= 0)
				{
					continue;
				}

				int level = (levelOverride > 0) ? levelOverride : row.Level;
				if (level <= 0)
				{
					level = 1;
				}

				int count = (row.Count > 0) ? row.Count : 1;
				for (int n = 0; n < count; n++)
				{
					// 슬롯보다 몬스터가 많으면 순환 재사용한다 (설계 13장).
					Vector3 pos = resolvePosition(slots, slotCursor);
					slotCursor++;

					MonsterSpawnManager.Instance.SpawnOneShot(row.MonsterID, level, pos);
				}
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static IReadOnlyList<DungeonSpawnSlot> getSlots()
		{
			if (MapManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<DungeonSpawnSlot> slots = MapManager.Instance.GetSlotsOfCurrentMap();
			if (slots.Count == 0)
			{
				Debug.LogWarning("[SpawnGroupRunner] 맵에 DungeonSpawnSlot 이 없습니다 — 히어로 주변에 배치합니다.");
				return null;
			}

			return slots;
		}

		private static Vector3 resolvePosition(IReadOnlyList<DungeonSpawnSlot> slots, int index)
		{
			Vector3 desired;
			if (slots != null && slots.Count > 0)
			{
				desired = slots[index % slots.Count].RandomPosition();
			}
			else
			{
				desired = randomAround(fallbackBasePos());
			}

			if (MapManager.HasInstance == false)
			{
				return desired;
			}

			// 벽 속 스폰 방지
			return MapManager.Instance.ResolveSpawnPosition(desired, SpawnClearance);
		}

		private static Vector3 fallbackBasePos()
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
