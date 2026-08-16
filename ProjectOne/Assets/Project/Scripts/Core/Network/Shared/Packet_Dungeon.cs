namespace ProjectOne.Shared
{
	// 던전 관련 패킷 — 클리어 보상 청구 등.

	// 서버가 상자를 열어 확정한 실제 획득 1건.
	// rewardType = RewardType 정수. Currency 는 itemId=Currency 정수, count=수량.
	[System.Serializable]
	public class GrantedRewardDto
	{
		public int rewardType;
		public int itemId;
		public int count;
		public bool isBonus;
	}

	// 던전 클리어 — 클라는 "어느 던전의 몇 단계를 깼다"만 보낸다.
	// 보상은 서버가 DungeonStage.RewardGroupID → Reward 로 직접 굴린다(서버 권위).
	// cleared=false 는 실패(로그용, 지급 없음).
	[System.Serializable]
	public class DungeonClearRequest
	{
		public int dungeonType;		// EDT.Dungeon 정수
		public int stage;
		public bool cleared;
	}

	// exp = 보상 가산 후 캐릭터의 누적 경험치(권위값). rewards = 서버가 확정한 실제 획득 목록.
	[System.Serializable]
	public class DungeonClearResponse : ServerResponse
	{
		public int exp;
		public GrantedRewardDto[] rewards;
	}
}
