using EDT;

namespace ProjectOne.UI
{
	// 아이템 등급의 표시명.
	//
	// 등급명 전용 테이블이 없어 지금은 한글을 여기에 직접 둔다.
	// **로컬라이징이 들어오면 이 스위치를 키 반환으로 바꾸고, Get 이 그 키로 문자열을 조회하게 한다** —
	// 조회 지점이 이 한 곳뿐이라 호출처는 그대로 두고 내부만 갈아끼우면 된다.
	public static class ItemGradeNames
	{
		// 등급 표시명. 모르는 값이면 enum 이름을 그대로 반환(폴백).
		public static string Get(ItemGradeType grade)
		{
			switch (grade)
			{
				case ItemGradeType.Normal:		return "일반";
				case ItemGradeType.Magic:		return "고급";
				case ItemGradeType.Rare:		return "희귀";
				case ItemGradeType.Epic:		return "영웅";
				case ItemGradeType.Legendary:	return "전설";
				case ItemGradeType.Mythic:		return "신화";
			}

			return grade.ToString();
		}
	}
}
