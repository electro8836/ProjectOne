namespace ProjectOne.UserData
{
	// 보유 카드스킬 1종(클라 런타임). 직렬화 DTO(OwnedCardSkillDto)와 분리 — 클라 전용 필드는 여기에 추가한다.
	// enchantLevel: 로비 강화로 도달한 레벨(= 던전에서 구매 가능한 최대 레벨의 기준). 최소 1.
	public sealed class OwnedCardSkill
	{
		public int cardSkillId;
		public int ownedCount;
		public int enchantLevel;
	}
}
