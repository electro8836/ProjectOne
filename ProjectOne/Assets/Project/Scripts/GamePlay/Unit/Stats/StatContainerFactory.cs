using System.Collections.Generic;
using EDT;

namespace ProjectOne.Unit.Stats
{
	// 캐릭터/몬스터의 Base 레이어를 테이블에서 읽어 StatContainer 를 만든다.
	// 성장식은 양쪽 동일: 값 = BaseValue + PerLevel × (Level - 1)
	// (CharacterStat 은 캐릭터가 하나뿐이라 그룹 키가 없고, MonsterStat 만 GroupID 로 묶인다)
	public static class StatContainerFactory
	{
		public static StatContainer ForCharacter(int level)
		{
			StatContainer c = new StatContainer();

			List<Table_CharacterStat.Row> all = new List<Table_CharacterStat.Row>(Table_CharacterStat.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				applyRow(c, all[i].StatDetailID, all[i].BaseValue, all[i].PerLevel, level);
			}

			return c;
		}

		public static StatContainer ForMonster(int statGroupId, int level)
		{
			StatContainer c = new StatContainer();
			if (statGroupId <= 0)
			{
				return c;
			}

			List<Table_MonsterStat.Row> all = new List<Table_MonsterStat.Row>(Table_MonsterStat.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].GroupID != statGroupId)
				{
					continue;
				}

				applyRow(c, all[i].StatDetailID, all[i].BaseValue, all[i].PerLevel, level);
			}

			return c;
		}

		private static void applyRow(StatContainer c, StatDetail detail, float baseValue, float perLevel, int level)
		{
			if (detail == StatDetail.None)
			{
				return;
			}

			int lv = level > 1 ? level : 1;
			c.SetBase(detail, baseValue + perLevel * (lv - 1));
		}
	}
}
