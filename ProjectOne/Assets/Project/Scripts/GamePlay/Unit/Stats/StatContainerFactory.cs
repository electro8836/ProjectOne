using EDT;

namespace ProjectOne.Unit.Stats
{
	// BaseStatID → Table_BaseStat 조회 → StatContainer 생성 (모든 유닛 공통 경로)
	public static class StatContainerFactory
	{
		public static StatContainer FromBaseStatID(int baseStatId)
		{
			var c = new StatContainer();

			Table_BaseStat.Row row = Table_BaseStat.Get(baseStatId);
			if (row == null)
			{
				return c;
			}

			c.SetBase(StatTypes.ATK_Base,             row.ATK_Base);
			c.SetBase(StatTypes.MATK_Base,            row.MATK_Base);
			c.SetBase(StatTypes.CritRate_Base,        row.CritRate_Base);
			c.SetBase(StatTypes.CritDam_Base,         row.CritDam_Base);
			c.SetBase(StatTypes.HP_Base,              row.MaxHP_Base);
			c.SetBase(StatTypes.DEF_Base,             row.DEF_Base);
			c.SetBase(StatTypes.MDEF_Base,            row.MDEF_Base);
			c.SetBase(StatTypes.HP_Regen_Base,        row.HpRecovery_Base);
			c.SetBase(StatTypes.AtkSpeed_Base,        row.AtkSpeed_Base);
			c.SetBase(StatTypes.MoveSpeed_Base,       row.MoveSpeed_Base);
			c.SetBase(StatTypes.Accuracy_Base,        row.Accuracy_Base);
			c.SetBase(StatTypes.BreakGage_Base,       row.BreakGage_Base);
			c.SetBase(StatTypes.BreakDamage_Base,     row.BreakDamage_Base);
			c.SetBase(StatTypes.BreakRecovery_Base,   row.BreakRecovery_Base);
			c.SetBase(StatTypes.Evasion_Base,         row.Evasion_Base);

			return c;
		}
	}
}
