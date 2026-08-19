using System;
using System.Collections.Generic;
using System.Globalization;
using EDT;
using UnityEngine;

namespace ProjectOne.Quests
{
	// 퀘스트 / NPC 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// QuestParam_1~3 은 자유 형식 문자열이고 QuestTargetType 마다 뜻이 다르다 (설계 3.2).
	// 슬롯의 뜻은 그 표가 전부이며, 타입 간 공통 의미는 없다 — SkillEffectParams 와 같은 방식이다.
	// 판정 때마다 문자열을 파싱하지 않도록 Build 시점에 강타입으로 굽는다.
	//
	// RewardCatalog / ConsumableCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class QuestCatalog
	{
		// 파싱이 끝난 퀘스트 1개. 목표 타입별로 쓰는 필드가 다르다.
		public sealed class BakedQuest
		{
			public Table_Quest.Row row;

			// KillMonster
			public int monsterId;
			public int killCount;
			public int limitMapId;			// 0이면 지역 무관 (설계 3.3)

			// EquipItemGrade / EquipItemLevel — 판정은 항상 "이상"
			public ItemGradeType reqGrade;
			public int reqEquipLevel;
			public EquipSlotTypes reqSlot;	// None 이면 슬롯 무관

			// Talk
			public int talkNpcId;

			// DungeonClear
			public EDT.Dungeon dungeon;
			public int dungeonStage;

			// ReachLevel
			public int reachLevel;

			// 파싱에 실패한 퀘스트는 진행 대상에서 제외한다. 경고는 Build 가 이미 냈다.
			public bool isValid;
		}

		private static readonly Dictionary<int, BakedQuest> _byId = new Dictionary<int, BakedQuest>();

		// 메인 체인 — ID 오름차순. ID 가 곧 진행 순서 데이터다 (설계 3.1).
		private static readonly List<BakedQuest> _mainChain = new List<BakedQuest>();

		// NPC 역인덱스 — 퀘스트 보유 여부는 NpcType 이 아니라 이 인덱스로 판정한다 (설계 5.2).
		private static readonly Dictionary<int, List<BakedQuest>> _acceptByNpc = new Dictionary<int, List<BakedQuest>>();
		private static readonly Dictionary<int, List<BakedQuest>> _completeByNpc = new Dictionary<int, List<BakedQuest>>();

		// 대사 — (NpcID, QuestID, Trigger) → Text. QuestID 0 은 퀘스트 무관 기본 대사다 (설계 5.4).
		private static readonly Dictionary<long, string> _dialogs = new Dictionary<long, string>();

		// 맵별 NPC 배치
		private static readonly Dictionary<int, List<Table_NpcSpawn.Row>> _spawnsByMap = new Dictionary<int, List<Table_NpcSpawn.Row>>();

		private static readonly List<BakedQuest> _emptyQuests = new List<BakedQuest>();
		private static readonly List<Table_NpcSpawn.Row> _emptySpawns = new List<Table_NpcSpawn.Row>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byId.Clear();
			_mainChain.Clear();
			_acceptByNpc.Clear();
			_completeByNpc.Clear();
			_dialogs.Clear();
			_spawnsByMap.Clear();

			buildQuests();
			buildDialogs();
			buildSpawns();

			_built = true;
			Debug.Log($"[QuestCatalog] 구축 완료 — 퀘스트:{_byId.Count}(메인 {_mainChain.Count}) 대사:{_dialogs.Count} NPC:{Table_Npc.All().Count} 배치:{Table_NpcSpawn.All().Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static BakedQuest Get(int questId)
		{
			BakedQuest baked;
			_byId.TryGetValue(questId, out baked);
			return baked;
		}

		public static IReadOnlyList<BakedQuest> MainChain
		{
			get { return _mainChain; }
		}

		// ReqQuestID 가 비어 있는 Main 퀘스트가 시작 퀘스트다 (설계 4.1).
		// 클리어한 마지막 메인 ID 를 넘기면 그 다음 메인을 돌려준다. 없으면 null(= 마지막까지 깼다).
		public static BakedQuest GetNextMain(int clearedMainQuestId)
		{
			for (int i = 0; i < _mainChain.Count; i++)
			{
				if (_mainChain[i].row.ReqQuestID == clearedMainQuestId)
				{
					return _mainChain[i];
				}
			}

			return null;
		}

		public static IReadOnlyList<BakedQuest> GetAcceptableAt(int npcId)
		{
			List<BakedQuest> list;
			if (_acceptByNpc.TryGetValue(npcId, out list) == true)
			{
				return list;
			}

			return _emptyQuests;
		}

		public static IReadOnlyList<BakedQuest> GetCompletableAt(int npcId)
		{
			List<BakedQuest> list;
			if (_completeByNpc.TryGetValue(npcId, out list) == true)
			{
				return list;
			}

			return _emptyQuests;
		}

		// 대사. 없으면 null — 호출자가 Default 로 폴백한다 (설계 5.5).
		public static string GetDialog(int npcId, int questId, DialogTriggerType trigger)
		{
			string text;
			_dialogs.TryGetValue(dialogKey(npcId, questId, trigger), out text);
			return text;
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
		public static bool IsSpawnActive(Table_NpcSpawn.Row row, int clearedMainQuestId)
		{
			if (row == null)
			{
				return false;
			}

			if (clearedMainQuestId < row.SpawnStartQuestID)
			{
				return false;
			}

			if (row.UseSpawnEnd == true && clearedMainQuestId >= row.SpawnEndQuestID)
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

				if (row.Category == QuestCategory.Main)
				{
					_mainChain.Add(baked);
				}

				if (row.AcceptType == QuestAcceptType.Npc && row.AcceptNpcID > 0)
				{
					addToNpcIndex(_acceptByNpc, row.AcceptNpcID, baked);
				}

				if (row.CompleteType == QuestCompleteType.Npc && row.CompleteNpcID > 0)
				{
					addToNpcIndex(_completeByNpc, row.CompleteNpcID, baked);
				}
			}

			_mainChain.Sort(compareById);
		}

		private static int compareById(BakedQuest a, BakedQuest b)
		{
			return a.row.ID.CompareTo(b.row.ID);
		}

		private static void addToNpcIndex(Dictionary<int, List<BakedQuest>> index, int npcId, BakedQuest baked)
		{
			List<BakedQuest> list;
			if (index.TryGetValue(npcId, out list) == false)
			{
				list = new List<BakedQuest>(2);
				index[npcId] = list;
			}

			list.Add(baked);
		}

		private static BakedQuest bake(Table_Quest.Row row)
		{
			BakedQuest baked = new BakedQuest();
			baked.row = row;
			baked.isValid = true;

			switch (row.QuestTargetType)
			{
				case QuestTargetType.KillMonster:
					baked.monsterId = parseInt(row.QuestParam_1, 0);
					baked.killCount = parseInt(row.QuestParam_2, 0);
					baked.limitMapId = parseInt(row.QuestParam_3, 0);
					baked.isValid = baked.monsterId > 0 && baked.killCount > 0;
					break;

				case QuestTargetType.EquipItemGrade:
					baked.reqGrade = parseEnum<ItemGradeType>(row.QuestParam_1);
					baked.reqSlot = parseEnum<EquipSlotTypes>(row.QuestParam_2);
					baked.isValid = baked.reqGrade != ItemGradeType.None;
					break;

				case QuestTargetType.EquipItemLevel:
					baked.reqEquipLevel = parseInt(row.QuestParam_1, 0);
					baked.reqSlot = parseEnum<EquipSlotTypes>(row.QuestParam_2);
					baked.isValid = baked.reqEquipLevel > 0;
					break;

				case QuestTargetType.Talk:
					baked.talkNpcId = parseInt(row.QuestParam_1, 0);
					baked.isValid = baked.talkNpcId > 0;
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

		private static void buildDialogs()
		{
			Dictionary<int, Table_NpcDialog.Row> all = Table_NpcDialog.All();
			Dictionary<int, Table_NpcDialog.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_NpcDialog.Row row = e.Current.Value;
				if (row.NpcID <= 0 || row.TriggerType == DialogTriggerType.None)
				{
					continue;
				}

				_dialogs[dialogKey(row.NpcID, row.QuestID, row.TriggerType)] = row.Text;
			}
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

		// (NpcID, QuestID, Trigger) 를 long 하나로 접는다 — 값 튜플 키의 GC 할당을 피한다.
		private static long dialogKey(int npcId, int questId, DialogTriggerType trigger)
		{
			return ((long)npcId << 40) ^ ((long)questId << 8) ^ (long)trigger;
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
		// 경고 목록이 곧 채워야 할 엑셀 작업 지시서다. 컨버터로의 승격은 STEP 15.

		private static void validate()
		{
			int issues = 0;
			issues += validateChain();
			issues += validateQuests();
			issues += validateNpcs();

			if (issues > 0)
			{
				Debug.LogWarning($"[QuestCatalog] 데이터 정합성 문제 {issues}건 — 위 경고 목록이 채워야 할 엑셀 작업입니다.");
			}
		}

		// 체인 무결성. NextQuestID 를 두지 않으므로 이 검증이 선형 진행을 담보하는 유일한 장치다 (설계 6장).
		private static int validateChain()
		{
			if (_mainChain.Count == 0)
			{
				return 0;
			}

			int issues = 0;
			int startCount = 0;

			Dictionary<int, int> byReq = new Dictionary<int, int>();
			for (int i = 0; i < _mainChain.Count; i++)
			{
				Table_Quest.Row row = _mainChain[i].row;

				if (row.ReqQuestID == 0)
				{
					startCount++;
					continue;
				}

				BakedQuest prev = Get(row.ReqQuestID);
				if (prev == null || prev.row.Category != QuestCategory.Main)
				{
					Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 ReqQuestID {row.ReqQuestID} 가 Main 퀘스트가 아닙니다.");
					issues++;
				}

				int already;
				if (byReq.TryGetValue(row.ReqQuestID, out already) == true)
				{
					Debug.LogWarning($"[QuestCatalog] Main 퀘스트 {already} 와 {row.ID} 가 같은 ReqQuestID {row.ReqQuestID} 를 가집니다 — 체인이 분기하면 선형 진행이 깨집니다.");
					issues++;
					continue;
				}

				byReq[row.ReqQuestID] = row.ID;
			}

			if (startCount != 1)
			{
				Debug.LogWarning($"[QuestCatalog] ReqQuestID 가 빈 Main 퀘스트가 {startCount}개입니다 — 정확히 1개여야 합니다.");
				issues++;
			}

			return issues;
		}

		private static int validateQuests()
		{
			int issues = 0;

			Dictionary<int, BakedQuest>.Enumerator e = _byId.GetEnumerator();
			while (e.MoveNext() == true)
			{
				BakedQuest baked = e.Current.Value;
				Table_Quest.Row row = baked.row;

				if (row.Category == QuestCategory.Main && row.IsRepeatable == true)
				{
					Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 는 Main 인데 IsRepeatable 이 TRUE 입니다 — Sub 만 허용됩니다.");
					issues++;
				}

				if (row.AcceptType == QuestAcceptType.Npc && row.AcceptNpcID <= 0)
				{
					Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 AcceptType 이 Npc 인데 AcceptNpcID 가 비었습니다.");
					issues++;
				}

				if (row.CompleteType == QuestCompleteType.Npc && row.CompleteNpcID <= 0)
				{
					Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 CompleteType 이 Npc 인데 CompleteNpcID 가 비었습니다.");
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
				case QuestTargetType.KillMonster:
					if (Table_Monster.Get(baked.monsterId) == null)
					{
						Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 몬스터 {baked.monsterId} 가 Monster 테이블에 없습니다.");
						issues++;
					}

					if (baked.limitMapId > 0 && Table_Map.Get(baked.limitMapId) == null)
					{
						Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 지역 한정 맵 {baked.limitMapId} 가 Map 테이블에 없습니다.");
						issues++;
					}

					break;

				case QuestTargetType.Talk:
					if (Table_Npc.Get(baked.talkNpcId) == null)
					{
						Debug.LogWarning($"[QuestCatalog] Quest {row.ID} 의 대화 대상 {baked.talkNpcId} 가 Npc 테이블에 없습니다.");
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

			// CompleteType != Npc 인데 QuestComplete 대사가 있으면 영원히 표시되지 않는다 (설계 6장).
			Dictionary<int, Table_NpcDialog.Row> dialogs = Table_NpcDialog.All();
			Dictionary<int, Table_NpcDialog.Row>.Enumerator de = dialogs.GetEnumerator();
			while (de.MoveNext() == true)
			{
				Table_NpcDialog.Row row = de.Current.Value;
				if (row.TriggerType != DialogTriggerType.QuestComplete || row.QuestID <= 0)
				{
					continue;
				}

				BakedQuest quest = Get(row.QuestID);
				if (quest == null)
				{
					Debug.LogWarning($"[QuestCatalog] NpcDialog {row.ID} 의 QuestID {row.QuestID} 가 Quest 테이블에 없습니다.");
					issues++;
					continue;
				}

				if (quest.row.CompleteType != QuestCompleteType.Npc)
				{
					Debug.LogWarning($"[QuestCatalog] NpcDialog {row.ID} 는 QuestComplete 인데 Quest {row.QuestID} 의 CompleteType 이 {quest.row.CompleteType} 입니다 — 표시되지 않습니다.");
					issues++;
				}
			}

			return issues;
		}
	}
}
