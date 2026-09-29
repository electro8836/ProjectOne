using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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

		// SpawnOneFromGroupAsync 호출 간에 유지되는 순환 커서.
		// 나눠서 소환해도 그룹의 Count 비율과 슬롯 분배가 한쪽으로 치우치지 않게 한다.
		private static int _flatCursor;

		// 그룹의 모든 행을 즉시 소환한다.
		// levelOverride > 0 이면 MonsterSpawn.Level 대신 그 값을 쓴다 (DungeonStage.MonsterLevel).
		public static void SpawnGroup(int groupId, int levelOverride)
		{
			if (groupId <= 0)
			{
				return;
			}

			SpawnGroupAt(groupId, levelOverride, getSlots());
		}

		// 그룹의 모든 행을 지정한 슬롯들에만 즉시 소환한다 — 미궁 트리거처럼 구역별로 슬롯이 나뉜 모드용.
		// slots 가 비어 있으면 히어로 주변에 배치한다.
		public static void SpawnGroupAt(int groupId, int levelOverride, IReadOnlyList<DungeonSpawnSlot> slots)
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

					MonsterSpawnManager.Instance.SpawnOneShot(row.MonsterID, level, pos, row.RewardGroupID);
				}
			}
		}

		// 그룹을 Count 만큼 펼친 가상 배열을 커서가 순환하며 1마리를 소환하고 생성된 개체를 돌려준다.
		// 균열 던전처럼 스폰 직후 개체에 손을 대야 하는 모드용 — 실패하면 null.
		public static async UniTask<Monster> SpawnOneFromGroupAsync(int groupId, int levelOverride)
		{
			if (groupId <= 0)
			{
				return null;
			}

			IReadOnlyList<Table_MonsterSpawn.Row> rows = MonsterCatalog.GetSpawnGroup(groupId);
			int total = countFlattened(rows);
			if (total <= 0)
			{
				Debug.LogWarning($"[SpawnGroupRunner] GroupID {groupId} 에 소환할 MonsterSpawn 행이 없습니다.");
				return null;
			}

			Table_MonsterSpawn.Row row = rowAtFlatIndex(rows, _flatCursor % total);
			Vector3 pos = resolvePosition(getSlots(), _flatCursor);
			_flatCursor++;

			if (row == null)
			{
				return null;
			}

			int level = (levelOverride > 0) ? levelOverride : row.Level;
			if (level <= 0)
			{
				level = 1;
			}

			return await MonsterSpawnManager.Instance.SpawnOneShotAsync(row.MonsterID, level, pos, row.RewardGroupID);
		}

		// 그룹을 펼쳤을 때의 총 마리 수 — 웨이브 마리 수의 분모다.
		public static int CountGroup(int groupId)
		{
			if (groupId <= 0)
			{
				return 0;
			}

			return countFlattened(MonsterCatalog.GetSpawnGroup(groupId));
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// SpawnGroup 과 같은 규칙(Count <= 0 이면 1마리)으로 그룹을 펼쳤을 때의 총 마리 수
		private static int countFlattened(IReadOnlyList<Table_MonsterSpawn.Row> rows)
		{
			int total = 0;
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i].MonsterID <= 0)
				{
					continue;
				}

				total += (rows[i].Count > 0) ? rows[i].Count : 1;
			}

			return total;
		}

		// 펼쳤을 때의 index 번째 개체가 속한 행. 행 수가 적어 선형 탐색으로 충분하다.
		private static Table_MonsterSpawn.Row rowAtFlatIndex(IReadOnlyList<Table_MonsterSpawn.Row> rows, int index)
		{
			for (int i = 0; i < rows.Count; i++)
			{
				Table_MonsterSpawn.Row row = rows[i];
				if (row.MonsterID <= 0)
				{
					continue;
				}

				int size = (row.Count > 0) ? row.Count : 1;
				if (index < size)
				{
					return row;
				}

				index -= size;
			}

			return null;
		}

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
			if (UnitManager.HasInstance == false)
			{
				return Vector3.zero;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
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
