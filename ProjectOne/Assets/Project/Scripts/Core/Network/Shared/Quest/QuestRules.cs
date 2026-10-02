using System;
using System.Collections.Generic;
using System.Globalization;
using EDT;

namespace ProjectOne.Shared
{
	// 퀘스트 목표 1개 — QuestParam_1~2 를 목표 타입별로 해석한 값.
	public struct QuestTarget
	{
		public QuestTargetType type;

		// MonsterKill / EliteKill / BossKill
		public int mapId;
		public int killCount;			// MonsterKill / EliteKill 만

		// DungeonClear
		public EDT.Dungeon dungeon;
		public int dungeonStage;

		// ReachLevel
		public int reachLevel;
	}

	// 퀘스트 규칙 중 테이블만 보고 판단하는 부분. 클라·서버 공용.
	//
	// QuestParam_1~2 는 자유 형식 문자열이고 QuestTargetType 마다 뜻이 다르다.
	//
	//   MonsterKill  Param1=MapID        Param2=처치 수   (그 맵에서 어떤 몬스터든 센다)
	//   EliteKill    Param1=MapID        Param2=처치 수   (엘리트만)
	//   BossKill     Param1=MapID        Param2=미사용
	//   DungeonClear Param1=던전타입     Param2=스테이지
	//   ReachLevel   Param1=히어로 레벨  Param2=미사용
	//
	// 체인은 ID 오름차순이다 — 클리어한 마지막 ID 보다 큰 것 중 가장 작은 ID 가 다음 퀘스트다.
	public static class QuestRules
	{
		// ID 오름차순 — 처음 쓸 때 만든다.
		private static List<int> _chain;

		// 목표 파라미터를 해석한다. 해석에 실패하면 false — 진행 대상에서 제외한다.
		public static bool TryParseTarget(Table_Quest.Row row, out QuestTarget target)
		{
			target = default(QuestTarget);
			if (row == null)
			{
				return false;
			}

			target.type = row.QuestTargetType;

			switch (row.QuestTargetType)
			{
				case QuestTargetType.MonsterKill:
				case QuestTargetType.EliteKill:
					target.mapId = parseInt(row.QuestParam_1, 0);
					target.killCount = parseInt(row.QuestParam_2, 0);
					return target.mapId > 0 && target.killCount > 0;

				case QuestTargetType.BossKill:
					target.mapId = parseInt(row.QuestParam_1, 0);
					return target.mapId > 0;

				case QuestTargetType.DungeonClear:
					target.dungeon = parseEnum<EDT.Dungeon>(row.QuestParam_1);
					target.dungeonStage = parseInt(row.QuestParam_2, 1);
					return target.dungeon != EDT.Dungeon.None;

				case QuestTargetType.ReachLevel:
					target.reachLevel = parseInt(row.QuestParam_1, 0);
					return target.reachLevel > 0;
			}

			return false;
		}

		// 진행도 분모. BossKill / DungeonClear 는 조건 충족형이라 1 이다.
		public static int GetRequiredCount(QuestTarget target)
		{
			switch (target.type)
			{
				case QuestTargetType.MonsterKill:
				case QuestTargetType.EliteKill:
					return target.killCount;

				case QuestTargetType.ReachLevel:
					return target.reachLevel;
			}

			return 1;
		}

		// 처치 카운터를 쓰는 목표인가 — 서버는 이 카운터를 클라 값으로 받는다.
		public static bool IsKillTarget(QuestTargetType type)
		{
			return type == QuestTargetType.MonsterKill || type == QuestTargetType.EliteKill || type == QuestTargetType.BossKill;
		}

		// 클리어한 마지막 퀘스트 다음 ID. 다 깼으면 0.
		public static int GetNextQuestId(int clearedQuestId)
		{
			List<int> chain = getChain();
			for (int i = 0; i < chain.Count; i++)
			{
				if (chain[i] > clearedQuestId)
				{
					return chain[i];
				}
			}

			return 0;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static List<int> getChain()
		{
			if (_chain != null)
			{
				return _chain;
			}

			List<int> chain = new List<int>();
			Dictionary<int, Table_Quest.Row>.Enumerator e = Table_Quest.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (e.Current.Value.ID > 0)
				{
					chain.Add(e.Current.Value.ID);
				}
			}

			chain.Sort();
			_chain = chain;
			return _chain;
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
	}
}
