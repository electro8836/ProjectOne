using ProjectOne.Shared;

namespace ProjectOne.Quests
{
	// 진행 중인 퀘스트 1개의 상태.
	//
	// counter 는 KillMonster 만 쓴다. 나머지 목표는 전부 조건 충족형(0/1)이라 저장할 진행도가 없고
	// 판정 시점에 다시 평가한다 (설계 3.3) — 그래야 이미 조건을 만족한 채로 수주해도 즉시 달성된다.
	public sealed class QuestProgress
	{
		public int questId;
		public int counter;

		public bool IsActive
		{
			get { return questId > 0; }
		}

		public void Clear()
		{
			questId = 0;
			counter = 0;
		}

		public void Begin(int id)
		{
			questId = id;
			counter = 0;
		}

		public QuestProgressDto ToDto()
		{
			QuestProgressDto dto = new QuestProgressDto();
			dto.questId = questId;
			dto.counter = counter;
			return dto;
		}

		public void LoadFrom(QuestProgressDto dto)
		{
			if (dto == null)
			{
				Clear();
				return;
			}

			questId = dto.questId;
			counter = dto.counter;
		}
	}
}
