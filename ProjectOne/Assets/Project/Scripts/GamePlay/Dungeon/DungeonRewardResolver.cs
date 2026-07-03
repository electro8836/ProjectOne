using System.Collections.Generic;
using UnityEngine;
using EDT;

namespace ProjectOne.Dungeon
{
	// 던전 보상 규칙 조회기. Table_DungeonReward 규칙에서 어떤 RewardItem(상자)을 줄지 선택한다.
	// 상자 오픈(등급/재화수량 결정)은 서버가 담당하므로 여기서 랜덤 해석은 하지 않는다.
	public static class DungeonRewardResolver
	{
		// (DungeonID, StageRound) 매칭 규칙 조회. 없으면 null.
		public static Table_DungeonReward.Row FindRule(int dungeonId, int round)
		{
			List<Table_DungeonReward.Row> all = new List<Table_DungeonReward.Row>(Table_DungeonReward.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				Table_DungeonReward.Row row = all[i];
				if (row.DungeonID == dungeonId && row.StageRound == round)
				{
					return row;
				}
			}

			return null;
		}

		// 롤 보상 풀에서 uniform 랜덤 1개(어떤 상자를 줄지 선택). 없으면 0.
		public static int PickRollRewardItemId(int dungeonId, int round)
		{
			Table_DungeonReward.Row rule = FindRule(dungeonId, round);
			if (rule == null || rule.StageRollRewardIDs.Length == 0)
			{
				return 0;
			}

			int index = Random.Range(0, rule.StageRollRewardIDs.Length);
			return rule.StageRollRewardIDs[index];
		}

		// 고정 보상 ID 배열(없으면 빈 배열).
		public static int[] GetFixRewardItemIds(int dungeonId, int round)
		{
			Table_DungeonReward.Row rule = FindRule(dungeonId, round);
			if (rule == null)
			{
				return System.Array.Empty<int>();
			}

			return rule.StageFixRewardIDs;
		}
	}
}
