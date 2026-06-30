using System.Collections.Generic;
using EDT;

namespace ProjectOne.UserData
{
	// 카드스킬 테이블에서 레벨(1~5)에 해당하는 스킬/가격, 강화 비용/최대레벨을 뽑는다. UI/전투/던전이 공유한다.
	// TraitSkillResolver 패턴 — 슬롯 레벨을 독립 SkillInfo 로 해석(런타임 배율 없음).
	public static class CardSkillResolver
	{
		// 카드스킬 레벨 범위
		public const int MinLevel = 1;
		public const int MaxLevel = 5;

		// 해금 여부 — UnlockClearDungeonID 가 0 이면 즉시 해금, 아니면 해당 던전 클리어 기록이 있어야 해금.
		public static bool IsUnlocked(Table_CardSkill.Row card)
		{
			if (card == null)
			{
				return false;
			}

			if (card.UnlockClearDungeonID == 0)
			{
				return true;
			}

			return Account.Instance.ClearedDungeons.IsCleared(card.UnlockClearDungeonID);
		}

		// 레벨(1~5) → 사용 스킬
		public static SkillInfo SkillForLevel(Table_CardSkill.Row card, int level)
		{
			if (card == null)
			{
				return SkillInfo.None;
			}

			switch (level)
			{
				case 1: return card.Skill_1;
				case 2: return card.Skill_2;
				case 3: return card.Skill_3;
				case 4: return card.Skill_4;
				case 5: return card.Skill_5;
				default: return card.Skill_1;
			}
		}

		// 레벨(1~5) → 던전 구매 가격(임시재화)
		public static int BuyPriceForLevel(Table_CardSkill.Row card, int level)
		{
			if (card == null)
			{
				return 0;
			}

			switch (level)
			{
				case 1: return card.BuyPrice_1;
				case 2: return card.BuyPrice_2;
				case 3: return card.BuyPrice_3;
				case 4: return card.BuyPrice_4;
				case 5: return card.BuyPrice_5;
				default: return card.BuyPrice_1;
			}
		}

		// 등급 + 목표레벨로 강화에 필요한 중복카드 개수. 해당 행이 없으면 0(더 강화 불가).
		public static int RequiredCountForEnchant(CardSkillGrade grade, int targetLevel)
		{
			Table_CardSkillEnchant.Row row = findEnchantRow(grade, targetLevel);
			if (row == null)
			{
				return 0;
			}

			return row.RequiredCount;
		}

		// 현재 강화레벨에서 던전 구매로 올릴 수 있는 최대 슬롯레벨. 행이 없으면 강화레벨 자체를 반환.
		public static int MaxShopLevel(CardSkillGrade grade, int enchantLevel)
		{
			Table_CardSkillEnchant.Row row = findEnchantRow(grade, enchantLevel);
			if (row == null)
			{
				return enchantLevel;
			}

			return row.MaxShopLevel;
		}

		// (SourceGrade == grade && TargetLevel == level) 행 조회. Dictionary 직접 순회 회피 위해 Values 복사.
		private static Table_CardSkillEnchant.Row findEnchantRow(CardSkillGrade grade, int level)
		{
			List<Table_CardSkillEnchant.Row> all = new List<Table_CardSkillEnchant.Row>(Table_CardSkillEnchant.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				Table_CardSkillEnchant.Row row = all[i];
				if (row.SourceGrade == grade && row.TargetLevel == level)
				{
					return row;
				}
			}

			return null;
		}
	}
}
