using EDT;

namespace ProjectOne.Skill
{
	// 스킬 설명 문구에 쓰는 이름 조회.
	// 스탯명은 Table_Stat 의 Name 컬럼이 소유한다 — 코드에 한글을 두면 테이블과 어긋난다.
	public static class SkillTextNames
	{
		// Stat 을 표시명으로. 테이블에 없으면 enum 이름을 그대로 반환(폴백).
		public static string StatName(Stat stat)
		{
			Table_Stat.Row row = Table_Stat.Get(stat);
			if (row == null || string.IsNullOrEmpty(row.Name) == true)
			{
				return stat.ToString();
			}

			return row.Name;
		}

		// 내부값이 0~1 배율인 스탯인가 — 표시할 때만 ×100 한다 (설계 3.5).
		public static bool IsPercentStat(Stat stat)
		{
			Table_Stat.Row row = Table_Stat.Get(stat);
			return row != null && row.ValueType == StatValueTypes.Percent;
		}

		// StatDetail 레이어의 표시 문구. DisplayFormat 의 {0} 자리에 값이 들어간다.
		// Percent 레이어는 저장값이 0.15 이고 표시가 15 이므로 여기서 ×100 한다.
		public static string FormatStatDetail(StatDetail detail, float value)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			if (row == null || string.IsNullOrEmpty(row.DisplayFormat) == true)
			{
				return detail + " " + value.ToString("0.##");
			}

			float shown = (row.StatValueType == StatValueTypes.Percent) ? value * 100f : value;
			return string.Format(row.DisplayFormat, shown.ToString("0.##"));
		}
	}
}
