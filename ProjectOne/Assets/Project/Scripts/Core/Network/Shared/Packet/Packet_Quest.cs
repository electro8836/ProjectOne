namespace ProjectOne.Shared
{
	// 퀘스트 패킷 — 완료(보상 지급 + 체인 전진), 처치 카운터 저장.

	// 진행 중 퀘스트 완료. counter 는 처치형 목표의 카운터다(서버가 신뢰한다).
	// 던전 클리어·레벨 목표는 서버가 저장값으로 직접 판정한다.
	[System.Serializable]
	public class QuestCompleteRequest
	{
		public int questId;
		public int counter;
	}

	[System.Serializable]
	public class QuestCompleteResponse : ServerResponse
	{
		public GrantedRewardDto[] rewards;			// 스택 아이템·재화
		public EquipmentInstanceDto[] equipments;	// 장비 — 서버가 UID·등급·품질을 확정한 인스턴스
		public int nextQuestId;						// 이어서 진행할 퀘스트(0 = 체인 끝)
	}

	// 처치 카운터 저장 — 앱 일시정지·종료 때 바뀐 경우에만 보낸다.
	[System.Serializable]
	public class SaveQuestProgressRequest
	{
		public int questId;
		public int counter;
	}

	[System.Serializable]
	public class SaveQuestProgressResponse : ServerResponse
	{
	}
}
