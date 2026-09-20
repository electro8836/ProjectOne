using System;
using System.Collections.Generic;
using System.Globalization;
using EDT;
using UnityEngine;

namespace ProjectOne.Quests
{
	// 퀘스트 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// QuestParam_1~2 는 자유 형식 문자열이고 QuestTargetType 마다 뜻이 다르다.
	//
	//   MonsterKill  Param1=MapID        Param2=처치 수   (그 맵에서 어떤 몬스터든 센다)
	//   BossKill     Param1=MapID        Param2=미사용
	//   DungeonClear Param1=던전타입     Param2=스테이지
	//   ReachLevel   Param1=히어로 레벨  Param2=미사용
	//
	// 슬롯의 뜻은 이 표가 전부이며 타입 간 공통 의미는 없다 — SkillEffectParams 와 같은 방식이다.
	// 판정 때마다 문자열을 파싱하지 않도록 Build 시점에 강타입으로 굽는다.
	//
	// RewardCatalog / ConsumableCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class QuestCatalog
	{
		// 파싱이 끝난 퀘스트 1개. 목표 타입별로 쓰는 필드가 다르다.
		public sealed class BakedQuest
		{
			public Table_Quest.Row row;

			// MonsterKill / BossKill
			public int mapId;
			public int killCount;			// MonsterKill 만

			// DungeonClear
			public EDT.Dungeon dungeon;
			public int dungeonStage;

			// ReachLevel
			public int reachLevel;

			// 파싱에 실패한 퀘스트는 진행 대상에서 제외한다. 경고는 Build 가 이미 냈다.
			public bool isValid;
		}

		private static readonly Dictionary<int, BakedQuest> _byId = new Dictionary<int, BakedQuest>();

		// 퀘스트 체인 — ID 오름차순. ID 가 곧 진행 순서 데이터다.
		private static readonly List<BakedQuest> _chain = new List<BakedQuest>();

		// 맵별 NPC 배치
		private static readonly Dictionary<int, List<Table_NpcSpawn.Row>> _spawnsByMap = new Dictionary<int, List<Table_NpcSpawn.Row>>();

		private static readonly List<Table_NpcSpawn.Row> _emptySpawns = new List<Table_NpcSpawn.Row>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byId.Clear();
			_chain.Clear();
			_spawnsByMap.Clear();

			buildQuests();
			buildSpawns();

			_built = true;
			Debug.Log($"[QuestCatalog] 구축 완료 — 퀘스트:{_byId.Count} NPC:{Table_Npc.All().Count} 배치:{Table_NpcSpawn.All().Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static BakedQuest Get(int questId)
		{
			BakedQuest baked;
			_byId.TryGetValue(questId, out baked);
			return baked;
		}

		public static IReadOnlyList<BakedQuest> Chain
		{
			get { return _chain; }
		}

		// 클리어한 마지막 퀘스트 ID 를 넘기면 그 다음 퀘스트를 돌려준다.
		// 없으면 null — 마지막까지 다 깼다는 뜻이다.
		public static BakedQuest GetNext(int clearedQuestId)
		{
			for (int i = 0; i < _chain.Count; i++)
			{
				if (_chain[i].row.ID > clearedQuestId)
				{
					return _chain[i];
				}
			}

			return null;
		}

		public static IReadOnlyList<Table_NpcSpawn.Row> GetSpawnsOfMap(int mapId)
		{
			List<Table_NpcSpawn.Row> list;
			if (_spawnsByMap.TryGetValue(mapId, out list) == true)
			{
				return list;
			}

			return _emptySpawns;
		}

		// NPC 등장 조건 (설계 5.3).
		//
		// UseSpawnEnd 를 반드시 먼저 본다. 빈칸은 0이라 `Cleared >= 0` 이 항상 참이고,
		// 이 한 줄을 빠뜨리면 모든 NPC 가 에러 없이 사라진다.
		public static bool IsSpawnActive(Table_NpcSpawn.Row row, int clearedQuestId)
		{
			if (row == null)
			{
				return false;
			}

			if (clearedQuestId < row.SpawnStartQuestID)
			{
				return false;
			}

			if (row.UseSpawnEnd == true && clearedQuestId >= row.SpawnEndQuestID)
			{
				return false;
			}

			return true;
		}

		// ── 구축 ──────────────────────────────────────────────────────

		private static void buildQuests()
		{
			Dictionary<int, Table_Quest.Row> all = Table_Quest.All();
			Dictionary<int, Table_Quest.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Quest.Row row = e.Current.Value;
				if (row.ID <= 0)
				{
					continue;
				}

				BakedQuest baked = bake(row);
				_byId[row.ID] = baked;
				_chain.Add(baked);
			}

			_chain.Sort(compareById);
		}

		private static int compareById(BakedQuest a, BakedQuest b)
		{
			return a.row.ID.CompareTo(b.row.ID);
		}

		private static BakedQuest bake(Table_Quest.Row row)
		{
			BakedQuest baked = new BakedQuest();
			baked.row = row;
			baked.isValid = true;

			switch (row.QuestTargetType)
			{
				case QuestTargetType.MonsterKill:
					baked.mapId = parseInt(row.QuestParam_1, 0);
					baked.killCount = parseInt(row.QuestParam_2, 0);
					baked.isValid = baked.mapId > 0 && baked.killCount > 0;
					break;

				case QuestTargetType.BossKill:
					baked.mapId = parseInt(row.QuestParam_1, 0);
					baked.isValid = baked.mapId > 0;
					break;

				case QuestTargetType.DungeonClear:
					baked.dungeon = parseEnum<EDT.Dungeon>(row.QuestParam_1);
					baked.dungeonStage = parseInt(row.QuestParam_2, 1);
					baked.isValid = baked.dungeon != EDT.Dungeon.None;
					break;

				case QuestTargetType.ReachLevel:
					baked.reachLevel = parseInt(row.QuestParam_1, 0);
					baked.isValid = baked.reachLevel > 0;
					break;

				default:
					baked.isValid = false;
					break;
			}

			return baked;
		}

		private static void buildSpawns()
		{
			Dictionary<int, Table_NpcSpawn.Row> all = Table_NpcSpawn.All();
			Dictionary<int, Table_NpcSpawn.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_NpcSpawn.Row row = e.Current.Value;
				if (row.MapID <= 0)
				{
					continue;
				}

				List<Table_NpcSpawn.Row> list;
				if (_spawnsByMap.TryGetValue(row.MapID, out list) == false)
				{
					list = new List<Table_NpcSpawn.Row>(4);
					_spawnsByMap[row.MapID] = list;
				}

				list.Add(row);
			}
		}

		private static T parseEnum<T>(string text) where T : struct
		{
			if (string.IsNullOrEmpty(text) == true)
			{
				return default(T);
			}

			T value;
			if (Enum.TryParse<T>(text.Trim(), false, out value) == true)
			{
				return value;
			}

			return default(T);
		}

		private static int parseInt(string text, int fallback)
		{
			if (string.IsNullOrEmpty(text) == true)
			{
				return fallback;
			}

			int value;
			if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) == true)
			{
				return value;
			}

			return fallback;
		}

		// ── 검증 ──────────────────────────────────────────────────────
		//
		// 경고 목록이 곧 채워야 할 엑셀 작업 지시서다.

		private static void validate()
		{
			int issues = 0;
			issues += validateQuests();
			issues += validateNpcs();

			if (issues > 0)
			{
				Debug.LogWarning($"[QuestCatalog] 데이터 정합성 문제 {issues}건 — 위 경고 목록이 채워야 할 엑셀 작업입니다.");
			}
		}

		private static int validateQuests()
		{
			int issues = 0;

			Dictionary<int, BakedQuest>.Enumerator e = _byId.GetEnumerator();
			while (e.MoveNext() == true)
			{
				BakedQuest baked = e.Current.Value;
				Table_Quest.Row row = baked.row;

				if (row.CompleteType == QuestCompleteType.None)
				{
					Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 CompleteType 이 비었습니다 — 수령 방식이 없어 영원히 완료되지 않습니다.");
					issues++;
				}

				if (baked.isValid == false)
				{
					Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 목표 파라미터를 {row.QuestTargetType} 으로 해석하지 못했습니다.");
					issues++;
					continue;
				}

				issues += validateTarget(baked);
			}

			return issues;
		}

		private static int validateTarget(BakedQuest baked)
		{
			int issues = 0;
			Table_Quest.Row row = baked.row;

			switch (row.QuestTargetType)
			{
				case QuestTargetType.MonsterKill:
				case QuestTargetType.BossKill:
					if (Table_Map.Get(baked.mapId) == null)
					{
						Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 맵 {baked.mapId} 가 Map 테이블에 없습니다.");
						issues++;
					}

					break;

				case QuestTargetType.DungeonClear:
					if (Table_Dungeon.Get(baked.dungeon) == null)
					{
						Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 던전 {baked.dungeon} 이 Dungeon 테이블에 없습니다.");
						issues++;
					}

					break;
			}

			if (row.RewardGroupID > 0 && Reward.RewardCatalog.GetGroup(row.RewardGroupID).Count == 0)
			{
				Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 보상 그룹 {row.RewardGroupID} 에 Reward 행이 없습니다.");
				issues++;
			}

			return issues;
		}

		private static int validateNpcs()
		{
			int issues = 0;

			Dictionary<int, Table_NpcSpawn.Row> spawns = Table_NpcSpawn.All();
			Dictionary<int, Table_NpcSpawn.Row>.Enumerator se = spawns.GetEnumerator();
			while (se.MoveNext() == true)
			{
				Table_NpcSpawn.Row row = se.Current.Value;

				if (Table_Npc.Get(row.NpcID) == null)
				{
					Debug.LogWarning($"[QuestCatalog] NpcSpawn {row.ID} 의 NpcID {row.NpcID} 가 Npc 테이블에 없습니다.");
					issues++;
				}

				if (Table_Map.Get(row.MapID) == null)
				{
					Debug.LogWarning($"[QuestCatalog] NpcSpawn {row.ID} 의 MapID {row.MapID} 가 Map 테이블에 없습니다.");
					issues++;
				}

				if (row.UseSpawnEnd == true && row.SpawnEndQuestID <= 0)
				{
					Debug.LogWarning($"[QuestCatalog] NpcSpawn {row.ID} 의 UseSpawnEnd 가 TRUE 인데 SpawnEndQuestID 가 비었습니다 — 즉시 사라집니다.");
					issues++;
				}
				else if (row.UseSpawnEnd == false && row.SpawnEndQuestID > 0)
				{
					Debug.LogWarning($"[QuestCatalog] NpcSpawn {row.ID} 의 SpawnEndQuestID 가 채워졌지만 UseSpawnEnd 가 FALSE 라 무시됩니다.");
					issues++;
				}
			}

			return issues;
		}
	}
}
