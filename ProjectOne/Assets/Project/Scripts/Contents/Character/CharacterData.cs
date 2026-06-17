using System.Collections.Generic;

namespace ProjectOne.ServerData
{
	// 장착 프리셋 — 무기/방어구/악세 3슬롯. 0 = 미장착. (Artifact 등 확장 시 필드 추가)
	[System.Serializable]
	public class EquipPreset
	{
		public int weaponItemId;
		public int armorItemId;
		public int accessoryItemId;
	}

	// 보유 캐릭터 1종 — 같은 characterId 를 유지하며 grade 만 상승(아이템 합성과 다름).
	// 성장 필드(level/exp/awakenLevel)와 등급(grade)/중복(dupCount)은 보관만 — 승급/레벨업 로직은 추후.
	[System.Serializable]
	public class OwnedCharacter
	{
		public int characterId;
		public int grade;        // 카드 등급 (중복 획득으로 상승)
		public int level;
		public int exp;
		public int awakenLevel;  // 각성레벨
		public int dupCount;     // 중복(조각) 카운트 — 등급 상승용 보관
		public EquipPreset preset = new EquipPreset();  // 캐릭터별 장착
	}

	// 캐릭터정보 도메인 데이터 — 보유 캐릭터 목록 + 현재 선택 캐릭터
	[System.Serializable]
	public class CharacterData
	{
		public List<OwnedCharacter> characters = new List<OwnedCharacter>();
		public int selectedCharacterId;
	}
}
