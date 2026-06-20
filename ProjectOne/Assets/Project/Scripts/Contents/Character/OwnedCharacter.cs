namespace ProjectOne.UserData
{
	// 보유 캐릭터 1종(클라 런타임). 직렬화 DTO(OwnedCharacterDto)와 분리 — 클라 전용 필드는 여기에 추가한다.
	public sealed class OwnedCharacter
	{
		public int characterId;
		public int grade;
		public int level;
		public int exp;
		public int awakenLevel;
		public int dupCount;
		public EquipPreset preset = new EquipPreset();
	}
}
