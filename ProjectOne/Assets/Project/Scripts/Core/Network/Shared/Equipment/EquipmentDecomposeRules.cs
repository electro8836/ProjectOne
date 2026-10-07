using EDT;

namespace ProjectOne.Shared
{
	// 일괄·자동 분해 조건 판정. 클라·서버 공용.
	//
	// 자동 분해는 양쪽이 같은 판정을 각자 내린다 — 필드 드랍은 클라가 먼저 굴려 반영하고 서버가 뒤에 정산하므로,
	// 판정이 다르면 한쪽에만 장비가 남는다.
	public static class EquipmentDecomposeRules
	{
		public static bool IsGradeSelected(DecomposeSettingDto setting, ItemGradeType grade)
		{
			return (setting.gradeMask & (1 << (int)grade)) != 0;
		}

		public static void SetGradeSelected(DecomposeSettingDto setting, ItemGradeType grade, bool selected)
		{
			if (selected == true)
			{
				setting.gradeMask |= 1 << (int)grade;
			}
			else
			{
				setting.gradeMask &= ~(1 << (int)grade);
			}
		}

		// 고른 등급이고, 품질 조건을 켰으면 품질이 기준 이하여야 한다. 등급을 하나도 고르지 않으면 대상이 없다.
		public static bool Matches(DecomposeSettingDto setting, ItemGradeType grade, int quality)
		{
			if (setting == null || IsGradeSelected(setting, grade) == false)
			{
				return false;
			}

			return setting.useQuality == false || quality <= setting.quality;
		}

		// 획득 즉시 분해할 장비인가. 갓 얻은 장비는 착용·잠금·보관 상태가 아니라 조건만 본다.
		public static bool IsAutoTarget(DecomposeSettingDto setting, ItemGradeType grade, int quality)
		{
			return setting != null && setting.auto == true && Matches(setting, grade, quality) == true;
		}
	}
}
