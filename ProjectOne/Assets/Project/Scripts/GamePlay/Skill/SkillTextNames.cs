using EDT;

namespace ProjectOne.Skill
{
	// 스킬 설명 자동 생성용 enum → 한국어 표시명 변환.
	// 스탯명은 Table_StatInfo 의 Name 컬럼을 사용한다. DamageType 은 별도 enum 이라 여기서 매핑한다.
	public static class SkillTextNames
	{
		// StatInfo 를 한국어 스탯명으로. 테이블 Name 사용, 없으면 enum 이름을 그대로 반환(폴백).
		public static string Stat(StatInfo stat)
		{
			Table_StatInfo.Row row = Table_StatInfo.Get(stat);
			return row != null && string.IsNullOrEmpty(row.Name) == false ? row.Name : stat.ToString();
		}

		// SkillDamageType 를 한국어로.
		public static string DamageType(SkillDamageType type)
		{
			switch (type)
			{
				case SkillDamageType.Physical: return "물리";
				case SkillDamageType.Magical: return "마법";
				case SkillDamageType.Pure: return "고정";
				default: return string.Empty;
			}
		}

		// 퍼센트 입력 스탯 여부 — 테이블 IsRatio 사용 (SkillEffectParams 의 IsPercentInput 과 동일 규칙).
		public static bool IsPercentStat(StatInfo stat)
		{
			Table_StatInfo.Row row = Table_StatInfo.Get(stat);
			return row != null && row.IsRatio == true;
		}
	}
}
