namespace ProjectOne.Shared
{
	// 펫·외형 패킷 — 펫 강화·승급, 펫 장착 + 코스튬 착용 저장. 판정·비용은 서버가 PetRules 로 직접 한다.

	// 펫 강화 묶음 — 장비 강화(EquipmentEnhanceRequest)와 같은 규칙이다.
	// 서버 레벨이 fromLevel 과 다르면 아무것도 하지 않고 현재 상태를 돌려준다.
	// 같으면 최대 count 회 반복하고, 재화가 모자라거나 상한에 닿으면 그 자리에서 멈춘다(부분 적용).
	[System.Serializable]
	public class PetEnhanceRequest
	{
		public int petId;
		public int fromLevel;
		public int count;
	}

	[System.Serializable]
	public class PetPromoteRequest
	{
		public int petId;
	}

	// 강화·승급 공용 응답 — 펫은 서버 최종 상태(level·grade), 재화는 차감량만 준다(EquipmentGrowthResponse 와 같은 이유).
	[System.Serializable]
	public class PetGrowthResponse : ServerResponse
	{
		public PetEntryDto pet;
		public CurrencyAmountDto[] spent;
	}

	// 펫 장착 + 코스튬 착용 저장 — 재화가 없는 "보이는 상태"라 화면 닫기·앱 일시정지 때 묶어 보낸다. 0 = 해제.
	[System.Serializable]
	public class SaveAppearanceRequest
	{
		public int equippedPetId;
		public int costumeWeaponId;
		public int costumeBodyId;
	}

	[System.Serializable]
	public class SaveAppearanceResponse : ServerResponse
	{
	}
}
