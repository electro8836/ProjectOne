using EDT;
using UnityEngine;
using ProjectOne.Currency;

namespace ProjectOne.UserData
{
	// 전투 클리어 보상 지급 — MapInfo.ClearRewardIDs(= Table_Reward ID들)를 해석해
	// 경험치/골드/재료/장비를 지급한다. 현재 클라 즉시 지급(추후 서버 권위로 교체 가능).
	// - Experience/Gold: Probability 단일 판정 → 성공 시 Min~Max 만큼 획득
	// - Material/Equipment: SourceIDs 의 itemId 마다 Probability 개별 판정 → 성공 시 Min~Max 개 획득
	public static class RewardService
	{
		public static void GrantClearRewards(int[] rewardIds, int characterId)
		{
			if (rewardIds == null)
			{
				return;
			}

			for (int i = 0; i < rewardIds.Length; i++)
			{
				Table_Reward.Row row = Table_Reward.Get(rewardIds[i]);
				if (row == null)
				{
					continue;
				}

				grantOne(row, characterId);
			}
		}

		private static void grantOne(Table_Reward.Row row, int characterId)
		{
			switch (row.RewardType)
			{
				case RewardTypes.Experience:
					if (roll(row.Probability) == true)
					{
						Account.Instance.Loadout.AddExp(characterId, randomCount(row.MinCount, row.MaxCount));
					}
					break;

				case RewardTypes.Gold:
					if (roll(row.Probability) == true)
					{
						CurrencyManager.Instance.Add(CurrencyInfo.Gold, randomCount(row.MinCount, row.MaxCount));
					}
					break;

				case RewardTypes.Material:
				case RewardTypes.Equipment:
					grantItems(row);
					break;

				default:
					break;
			}
		}

		// SourceIDs 의 각 itemId 마다 개별 확률 판정 → 성공 시 Min~Max 개 인벤토리 지급
		private static void grantItems(Table_Reward.Row row)
		{
			if (row.SourceIDs == null)
			{
				return;
			}

			for (int i = 0; i < row.SourceIDs.Length; i++)
			{
				int itemId = row.SourceIDs[i];
				if (itemId <= 0)
				{
					continue;
				}

				if (roll(row.Probability) == false)
				{
					continue;
				}

				Account.Instance.Inventory.Add(itemId, randomCount(row.MinCount, row.MaxCount));
			}
		}

		private static bool roll(float probability)
		{
			if (probability >= 1f)
			{
				return true;
			}

			if (probability <= 0f)
			{
				return false;
			}

			return Random.value <= probability;
		}

		// Min~Max 포함 정수 (Max < Min 방어)
		private static int randomCount(int min, int max)
		{
			if (max < min)
			{
				max = min;
			}

			return Random.Range(min, max + 1);
		}
	}
}
