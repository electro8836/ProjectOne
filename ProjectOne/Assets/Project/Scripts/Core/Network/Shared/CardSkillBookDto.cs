using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 보유 카드스킬 1종(직렬화 DTO) — enchantLevel 은 로비 강화레벨(최소 1) 보관.
	[System.Serializable]
	public class OwnedCardSkillDto
	{
		public int cardSkillId;
		public int ownedCount;
		public int enchantLevel;
	}

	// 카드스킬 보유 직렬화 DTO — 서버-클라 공유 영속 스키마. 클라는 CardSkillBook 으로 변환해 사용한다.
	[System.Serializable]
	public class CardSkillBookDto
	{
		public List<OwnedCardSkillDto> cardSkills = new List<OwnedCardSkillDto>();
	}
}
