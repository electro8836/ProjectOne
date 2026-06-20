using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 장착 프리셋 DTO — 무기/방어구/악세 3슬롯. 0 = 미장착.
	[System.Serializable]
	public class EquipPresetDto
	{
		public int weaponItemId;
		public int armorItemId;
		public int accessoryItemId;
	}

	// 보유 캐릭터 1종 DTO — 같은 characterId 를 유지하며 grade 만 상승.
	[System.Serializable]
	public class OwnedCharacterDto
	{
		public int characterId;
		public int grade;
		public int level;
		public int exp;
		public int awakenLevel;
		public int dupCount;
		public EquipPresetDto preset = new EquipPresetDto();
	}

	// 캐릭터 직렬화 DTO — 서버-클라 공유 스키마(현재 서버 미사용, 향후 동기화 대비).
	// 클라는 Loadout 으로 변환해 사용한다.
	[System.Serializable]
	public class CharacterDto
	{
		public List<OwnedCharacterDto> characters = new List<OwnedCharacterDto>();
		public int selectedCharacterId;
	}
}
