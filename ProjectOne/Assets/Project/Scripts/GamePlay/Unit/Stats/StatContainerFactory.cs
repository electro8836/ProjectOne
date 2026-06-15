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

			c.SetBase(StatInfo.ATK_Base,             row.ATK_Base);
			c.SetBase(StatInfo.MATK_Base,            row.MATK_Base);
			c.SetBase(StatInfo.CritRate_Base,        row.CritRate_Base);
			c.SetBase(StatInfo.CritDam_Base,         row.CritDam_Base);
			c.SetBase(StatInfo.MaxHP_Base,           row.MaxHP_Base);
			c.SetBase(StatInfo.DEF_Base,             row.DEF_Base);
			c.SetBase(StatInfo.MDEF_Base,            row.MDEF_Base);
			c.SetBase(StatInfo.HpRecovery_Base,      row.HpRecovery_Base);
			c.SetBase(StatInfo.AtkSpeed_Base,        row.AtkSpeed_Base);
			c.SetBase(StatInfo.MoveSpeed_Base,       row.MoveSpeed_Base);
			c.SetBase(StatInfo.Accuracy_Base,        row.Accuracy_Base);
			c.SetBase(StatInfo.BreakGage_Base,       row.BreakGage_Base);
			c.SetBase(StatInfo.BreakDamage_Base,     row.BreakDamage_Base);
			c.SetBase(StatInfo.BreakRecovery_Base,   row.BreakRecovery_Base);
			c.SetBase(StatInfo.KnockBack_Base,       row.KnockBack_Base);
			c.SetBase(StatInfo.KnockBackResist_Base, row.KnockBackResist_Base);
			c.SetBase(StatInfo.MaxStamina_Base,      row.MaxStamina_Base);
			c.SetBase(StatInfo.StaminaRegen_Base,    row.StaminaRegen_Base);
			c.SetBase(StatInfo.StaminaSteal_Base,    row.StaminaSteal_Base);
			c.SetBase(StatInfo.Evasion_Base,         row.Evasion_Base);

			return c;
		}
	}
}
