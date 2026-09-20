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
			ApplyCharacterBase(c, level);
			return c;
		}

		// 이미 쓰고 있는 컨테이너의 Base 레이어만 새 레벨로 덮어쓴다 — 레벨업 시 사용.
		// Base 는 절대 대입(SetBase)이라 다시 적용해도 누적되지 않고, 모디파이어(장비·트리·버프)는 그대로 남는다.
		public static void ApplyCharacterBase(StatContainer target, int level)
		{
			if (target == null)
			{
				return;
			}

			List<Table_CharacterStat.Row> all = new List<Table_CharacterStat.Row>(Table_CharacterStat.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				applyRow(target, all[i].StatDetailID, all[i].BaseValue, all[i].PerLevel, level);
			}
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
