using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 진행 중인 퀘스트 1개 (직렬화 DTO).
	// counter 는 KillMonster 만 쓴다 — 나머지 목표는 전부 조건 충족형(0/1)이라 매번 재평가한다.
	[System.Serializable]
	public class QuestProgressDto
	{
		public int questId;
		public int counter;
	}

	// 퀘스트 저장 DTO — 퀘스트 설계 4.4 의 저장 항목 그대로.
	//
	// 반복 서브 퀘스트의 클리어 이력은 저장하지 않는다. 무한 반복이라 쌓아 봐야 의미가 없고
	// 세이브 데이터만 늘어난다 (설계 4.4).
	[System.Serializable]
	public class QuestDto
	{
		// 클리어한 마지막 메인 퀘스트 ID. 활성화 조건과 NPC 등장 조건의 유일한 근거다.
		public int clearedMainQuestId;

		// 진행 중 메인 퀘스트. questId 가 0이면 진행 중이 아니다.
		public QuestProgressDto main = new QuestProgressDto();

		// 진행 중 서브 퀘스트 — 최대 2개 (설계 1.2).
		public List<QuestProgressDto> subs = new List<QuestProgressDto>();
	}
}
