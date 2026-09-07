using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 마스터리 1종의 진행도(직렬화 DTO) — 마스터리 설계 8.1 의 저장 항목 그대로.
	// 레벨은 totalExp 에서 파생되지만, 서버 권위 이관 시 검증 기준이 필요해 함께 싣는다.
	[System.Serializable]
	public class MasteryProgressDto
	{
		public int masteryId;		// WeaponMastery
		public int level = 1;
		public int totalExp;
		public int itemPointUsed;	// 지식의 서 사용 횟수 (0 ~ SkillPoint_Item.MaxPoint)

		// 투자 노드 — 두 배열이 같은 인덱스로 대응한다.
		// Dictionary 는 JsonUtility 가 직렬화하지 못해 배열 두 벌로 편다.
		public List<int> nodeIds = new List<int>();
		public List<int> nodeLevels = new List<int>();
	}

	// 마스터리 저장 DTO — SkillDto 를 대체한다.
	[System.Serializable]
	public class MasteryDto
	{
		public List<MasteryProgressDto> masteries = new List<MasteryProgressDto>();

		// 업적 포인트는 전역이며 전 마스터리가 각자 전액을 쓴다 (설계 7.1 — 나눠 쓰는 것이 아니다).
		public int achievementPoint;
	}
}
