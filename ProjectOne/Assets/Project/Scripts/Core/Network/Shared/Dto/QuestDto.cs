namespace ProjectOne.Shared
{
	// 진행 중인 퀘스트 1개 (직렬화 DTO).
	// counter 는 MonsterKill / BossKill 만 쓴다 — 나머지 목표는 조건 충족형(0/1)이라 매번 재평가한다.
	[System.Serializable]
	public class QuestProgressDto
	{
		public int questId;
		public int counter;
	}

	// 퀘스트 저장 DTO.
	//
	// 진행 중 퀘스트는 항상 1개이고 ID 오름차순이 곧 체인이라, 클리어한 마지막 ID 와
	// 진행 중 1개만 있으면 상태가 전부 복원된다.
	[System.Serializable]
	public class QuestDto
	{
		// 클리어한 마지막 퀘스트 ID. 다음 퀘스트 해금과 NPC 등장 조건의 유일한 근거다.
		public int clearedQuestId;

		// 진행 중 퀘스트. questId 가 0이면 진행 중이 아니다.
		public QuestProgressDto current = new QuestProgressDto();
	}
}
