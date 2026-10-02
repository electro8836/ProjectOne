namespace ProjectOne.Shared
{
	// 출석 패킷 — 오늘 몫 수령. 날짜 경계는 서버 시간(ResetDay) 기준이다.

	// typeId 는 (int)EDT.DailyBonusType, dayCount 는 받으려는 일차(서버의 다음 일차와 같아야 한다).
	[System.Serializable]
	public class DailyBonusClaimRequest
	{
		public int typeId;
		public int dayCount;
	}

	[System.Serializable]
	public class DailyBonusClaimResponse : ServerResponse
	{
		public GrantedRewardDto[] rewards;			// 스택 아이템·재화
		public EquipmentInstanceDto[] equipments;	// 장비 — 서버가 UID·등급·품질을 확정한 인스턴스
	}
}
