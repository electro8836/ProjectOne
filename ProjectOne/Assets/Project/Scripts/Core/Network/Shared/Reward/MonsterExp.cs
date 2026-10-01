using EDT;

namespace ProjectOne.Shared
{
	// 몬스터 처치 경험치 (몬스터 설계 9장). 클라·서버 공용.
	//
	//   경험치 = (BaseExp + PerLevelExp × (Level - 1)) × (1000 + 보너스퍼밀) / 1000   (정수 반올림)
	//
	// 보너스(Stat_ExpBonus)는 퍼밀 정수로 받는다 — 서버가 같은 값을 재계산할 때 실수 오차가 끼지 않게.
	public static class MonsterExp
	{
		// 보너스 적용 전 기준값. 몬스터가 없으면 0.
		public static int GetBase(int monsterId, int level)
		{
			Table_Monster.Row row = Table_Monster.Get(monsterId);
			if (row == null)
			{
				return 0;
			}

			int lv = level > 0 ? level : 1;
			return row.BaseExp + row.PerLevelExp * (lv - 1);
		}

		public static int Calc(int monsterId, int level, int expBonusPermille)
		{
			int baseExp = GetBase(monsterId, level);
			if (baseExp <= 0)
			{
				return 0;
			}

			return Permille.Apply(baseExp, expBonusPermille);
		}
	}
}
