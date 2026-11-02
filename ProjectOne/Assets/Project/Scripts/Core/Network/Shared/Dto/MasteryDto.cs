using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 마스터리 1종의 진행도(직렬화 DTO) — 마스터리 설계 8.1 의 저장 항목 그대로.
	// 레벨은 totalExp 에서 파생되지만, 콘솔 표시용으로 서버가 함께 써 둔다(클라는 읽지 않는다).
	[System.Serializable]
	public class MasteryProgressDto
	{
		public int masteryId;		// WeaponMastery
		public int level = 1;
		public int totalExp;

		// 스킬 슬롯 6칸([0] 패시브 · [1~4] 액티브 · [5] 궁극기) — 값은 EDT.Skill, 빈 칸은 0 (설계 7.1).
		public List<int> slots = new List<int>();
	}

	// 마스터리 저장 DTO.
	[System.Serializable]
	public class MasteryDto
	{
		public List<MasteryProgressDto> masteries = new List<MasteryProgressDto>();
	}
}
