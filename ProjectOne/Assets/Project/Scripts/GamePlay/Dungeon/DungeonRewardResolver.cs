using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 던전 보상 해석기. Table_DungeonReward 규칙 → 롤/고정 보상 선택(레이어1) →
	// Table_Reward_* 를 Weight 가중 랜덤으로 등급까지만 해석(레이어2)한다. 구체 아이템 특정/지급은 하지 않는다.
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

		// 롤 보상 풀에서 uniform 랜덤 1개(레이어1). 없으면 0.
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

		// 결과창 합산용 키 — RewardType별 등급/종류까지 같은 보상을 하나로 묶는다.
		// Equipment=등급, Material=종류+등급, Skill=등급, Currency=재화종류.
		public static string GradeKey(in DungeonRewardResult r)
		{
			switch (r.Type)
			{
			case RewardTypes.Equipment:
			{
				Table_Reward_Eqiupment.Row row = Table_Reward_Eqiupment.Get(r.SourceRowId);
				return "E|" + (row != null ? (int)row.Grade : 0);
			}
			case RewardTypes.Material:
			{
				Table_Reward_Material.Row row = Table_Reward_Material.Get(r.SourceRowId);
				return row != null ? ("M|" + (int)row.MaterialType + "|" + (int)row.MaterialGrade) : "M|0|0";
			}
			case RewardTypes.Skill:
			{
				Table_Reward_CardSkill.Row row = Table_Reward_CardSkill.Get(r.SourceRowId);
				return "S|" + (row != null ? (int)row.CardGrade : 0);
			}
			case RewardTypes.Currency:
			{
				Table_Reward_Currency.Row row = Table_Reward_Currency.Get(r.SourceRowId);
				return "C|" + (row != null ? (int)row.CurrencyType : 0);
			}
			default:
				return "?";
			}
		}

		// rewardItemId(레이어1 결과) → RewardType 별 소스 그룹을 Weight 가중 랜덤(레이어2)해 등급까지 해석.
		public static bool Resolve(int rewardItemId, out DungeonRewardResult result)
		{
			result = default(DungeonRewardResult);
			if (rewardItemId <= 0)
			{
				return false;
			}

			Table_RewardItem.Row item = Table_RewardItem.Get(rewardItemId);
			if (item == null)
			{
				return false;
			}

			switch (item.RewardType)
			{
			case RewardTypes.Equipment:
				return resolveEquipment(item, out result);
			case RewardTypes.Material:
				return resolveMaterial(item, out result);
			case RewardTypes.Skill:
				return resolveCardSkill(item, out result);
			case RewardTypes.Currency:
				return resolveCurrency(item, out result);
			default:
				return false;
			}
		}

		private static bool resolveEquipment(Table_RewardItem.Row item, out DungeonRewardResult result)
		{
			result = default(DungeonRewardResult);

			List<Table_Reward_Eqiupment.Row> rows = new List<Table_Reward_Eqiupment.Row>();
			List<int> weights = new List<int>();
			List<Table_Reward_Eqiupment.Row> all = new List<Table_Reward_Eqiupment.Row>(Table_Reward_Eqiupment.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].EquipmentSourceID == item.RewardSourceID)
				{
					rows.Add(all[i]);
					weights.Add(all[i].Weight);
				}
			}

			int pick = WeightedRandom.PickIndex(weights);
			if (pick < 0)
			{
				return false;
			}

			result = new DungeonRewardResult(item.ID, RewardTypes.Equipment, rows[pick].ID, 1);
			return true;
		}

		private static bool resolveMaterial(Table_RewardItem.Row item, out DungeonRewardResult result)
		{
			result = default(DungeonRewardResult);

			List<Table_Reward_Material.Row> rows = new List<Table_Reward_Material.Row>();
			List<int> weights = new List<int>();
			List<Table_Reward_Material.Row> all = new List<Table_Reward_Material.Row>(Table_Reward_Material.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].MaterialSourceID == item.RewardSourceID)
				{
					rows.Add(all[i]);
					weights.Add(all[i].Weight);
				}
			}

			int pick = WeightedRandom.PickIndex(weights);
			if (pick < 0)
			{
				return false;
			}

			result = new DungeonRewardResult(item.ID, RewardTypes.Material, rows[pick].ID, 1);
			return true;
		}

		private static bool resolveCardSkill(Table_RewardItem.Row item, out DungeonRewardResult result)
		{
			result = default(DungeonRewardResult);

			List<Table_Reward_CardSkill.Row> rows = new List<Table_Reward_CardSkill.Row>();
			List<int> weights = new List<int>();
			List<Table_Reward_CardSkill.Row> all = new List<Table_Reward_CardSkill.Row>(Table_Reward_CardSkill.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].CardSkillSourceID == item.RewardSourceID)
				{
					rows.Add(all[i]);
					weights.Add(all[i].Weight);
				}
			}

			int pick = WeightedRandom.PickIndex(weights);
			if (pick < 0)
			{
				return false;
			}

			result = new DungeonRewardResult(item.ID, RewardTypes.Skill, rows[pick].ID, 1);
			return true;
		}

		private static bool resolveCurrency(Table_RewardItem.Row item, out DungeonRewardResult result)
		{
			result = default(DungeonRewardResult);

			// 재화는 Weight 가 없어 uniform 추출 후 Min~Max 수량 롤.
			List<Table_Reward_Currency.Row> rows = new List<Table_Reward_Currency.Row>();
			List<Table_Reward_Currency.Row> all = new List<Table_Reward_Currency.Row>(Table_Reward_Currency.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].CurrencySourceID == item.RewardSourceID)
				{
					rows.Add(all[i]);
				}
			}

			if (rows.Count == 0)
			{
				return false;
			}

			Table_Reward_Currency.Row row = rows[Random.Range(0, rows.Count)];
			int amount = Random.Range(row.MinCount, row.MaxCount + 1);
			result = new DungeonRewardResult(item.ID, RewardTypes.Currency, row.ID, amount);
			return true;
		}
	}
}
