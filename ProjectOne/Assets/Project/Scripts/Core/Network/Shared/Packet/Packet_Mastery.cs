namespace ProjectOne.Shared
{
	// 마스터리 패킷 — 스킬 슬롯 저장.

	// 바뀐 마스터리의 슬롯 최종 상태. masteryId·slots 만 쓴다(경험치는 서버값을 믿는다).
	[System.Serializable]
	public class SaveMasterySlotsRequest
	{
		public MasteryProgressDto[] masteries;
	}

	[System.Serializable]
	public class SaveMasterySlotsResponse : ServerResponse
	{
	}
}
