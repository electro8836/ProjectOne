namespace ProjectOne.Shared
{
	// 장비 성장 패킷 — 강화·승급·전이. 판정·비용은 서버가 EquipmentGrowthRules 로 직접 한다.

	// 강화 묶음 — 클라가 미리 적용한 count 회를 한 번에 보낸다.
	// 서버 레벨이 fromLevel 과 다르면 아무것도 하지 않고 현재 상태를 돌려준다(클라가 그 상태로 맞춘다).
	// 같으면 최대 count 회 반복하고, 재화가 모자라거나 상한에 닿으면 그 자리에서 멈춘다(부분 적용).
	[System.Serializable]
	public class EquipmentEnhanceRequest
	{
		public long uid;
		public int fromLevel;
		public int count;
	}

	[System.Serializable]
	public class EquipmentPromoteRequest
	{
		public long uid;
	}

	// 두 장비의 등급·강화도를 교체한다.
	[System.Serializable]
	public class EquipmentTransferRequest
	{
		public long sourceUid;
		public long targetUid;
	}

	// 성장 3종 공용 응답.
	// 장비는 서버 최종 상태(grade·level)를 그대로 덮는다. 재화는 절대값이 아닌 차감량만 준다 —
	// 클라 재화에는 아직 배치로 보내지 않은 필드 처치분이 섞여 있어 서버 값으로 덮으면 그 몫이 사라진다.
	[System.Serializable]
	public class EquipmentGrowthResponse : ServerResponse
	{
		public EquipmentInstanceDto[] equipments;
		public CurrencyAmountDto[] spent;
	}
}
