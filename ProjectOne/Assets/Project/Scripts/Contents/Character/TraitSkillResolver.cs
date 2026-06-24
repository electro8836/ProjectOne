using EDT;

namespace ProjectOne.UserData
{
	// 특성그룹 행에서 슬롯 레벨(1~5)에 해당하는 스킬을 뽑는다. UI/전투가 공유한다.
	public static class TraitSkillResolver
	{
		public static SkillInfo SkillForLevel(Table_CharacterTrait.Row trait, int level)
		{
			if (trait == null)
			{
				return SkillInfo.None;
			}

			switch (level)
			{
				case 1: return trait.SkillID_1;
				case 2: return trait.SkillID_2;
				case 3: return trait.SkillID_3;
				case 4: return trait.SkillID_4;
				case 5: return trait.SkillID_5;
				default: return trait.SkillID_1;
			}
		}
	}
}
