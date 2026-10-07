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

	// 분해 — 장비를 없애고 주 재화 일부를 돌려받는다. 착용 중인 장비는 서버가 거절한다.
	[System.Serializable]
	public class EquipmentDecomposeRequest
	{
		public long uid;
	}

	// 재화는 성장 응답과 같은 이유로 절대값이 아닌 증가량만 준다.
	[System.Serializable]
	public class EquipmentDecomposeResponse : ServerResponse
	{
		public long uid;
		public CurrencyAmountDto[] gained;
	}

	// 일괄 분해 — 클라가 조건으로 고른 장비들. 서버는 하나씩 다시 확인해 착용·잠금 장비를 건너뛴다.
	[System.Serializable]
	public class EquipmentDecomposeAllRequest
	{
		public long[] uids;
	}

	// uids 는 실제로 분해된 장비다 — 요청보다 적을 수 있다.
	[System.Serializable]
	public class EquipmentDecomposeAllResponse : ServerResponse
	{
		public long[] uids;
		public CurrencyAmountDto[] gained;
	}

	// 잠금 저장 요청 — 잠근 장비 UID 전체(최종 상태)를 싣는다. 보관함 저장(SaveStashRequest)과 같은 방식이다.
	[System.Serializable]
	public class SaveEquipmentLockRequest
	{
		public long[] lockedUids;
	}

	[System.Serializable]
	public class SaveEquipmentLockResponse : ServerResponse
	{
	}

	// 분해 조건 저장 — 서버가 지급할 때 이 조건으로 자동 분해를 판정한다.
	[System.Serializable]
	public class SaveDecomposeSettingRequest
	{
		public DecomposeSettingDto setting;
	}

	[System.Serializable]
	public class SaveDecomposeSettingResponse : ServerResponse
	{
	}
}
