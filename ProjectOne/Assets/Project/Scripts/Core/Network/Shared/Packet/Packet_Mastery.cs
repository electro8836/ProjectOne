namespace ProjectOne.Shared
{
	// 마스터리 패킷 — 스킬트리 저장, 지식의 서 사용.

	// 바뀐 마스터리의 트리 최종 상태. masteryId·nodeIds·nodeLevels 만 쓴다(경험치·포인트는 서버값을 믿는다).
	[System.Serializable]
	public class SaveMasteryTreeRequest
	{
		public MasteryProgressDto[] trees;
	}

	[System.Serializable]
	public class SaveMasteryTreeResponse : ServerResponse
	{
	}

	// 지식의 서 1개 사용 — 서버가 아이템을 차감하고 대상 마스터리의 itemPointUsed 를 올린다.
	[System.Serializable]
	public class UseSkillPointItemRequest
	{
		public int itemId;
		public int masteryId;
	}

	[System.Serializable]
	public class UseSkillPointItemResponse : ServerResponse
	{
	}
}
