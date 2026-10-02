namespace ProjectOne.Shared
{
	// 히어로패스 패킷 — 레벨 보상 수령. 경험치는 서버가 처치·던전 클리어 정산 때 직접 센다.

	// 받을 보상 목록 — levels[i] 와 advanced[i] 가 한 쌍이다(모두 받기도 한 번에 보낸다).
	// 하나라도 받을 수 없으면 서버는 전체를 거절한다.
	[System.Serializable]
	public class HeroPassClaimRequest
	{
		public int[] levels;
		public bool[] advanced;
	}

	[System.Serializable]
	public class HeroPassClaimResponse : ServerResponse
	{
		public GrantedRewardDto[] rewards;			// 스택 아이템·재화
		public EquipmentInstanceDto[] equipments;	// 장비 — 서버가 UID·등급·품질을 확정한 인스턴스
	}
}
