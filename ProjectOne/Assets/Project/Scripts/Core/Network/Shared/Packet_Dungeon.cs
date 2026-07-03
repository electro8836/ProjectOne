namespace ProjectOne.Shared
{
	// 던전 관련 패킷 — 클리어 보상 청구 등.

	// 누적 상자 1건 — 클라가 던전 진행 중 모은 RewardItem(상자) 종류와 개수, 정상/보너스 여부.
	[System.Serializable]
	public class RewardBoxDto
	{
		public int rewardItemId;
		public int count;
		public bool isBonus;
	}

	// 서버가 상자를 열어 확정한 실제 획득 1건.
	// rewardType = RewardTypes 정수. Currency 는 itemId=CurrencyInfo 정수, count=수량.
	[System.Serializable]
	public class GrantedRewardDto
	{
		public int rewardType;
		public int itemId;
		public int count;
		public bool isBonus;
	}

	// 던전 클리어 — 클라가 던전ID + 전투 캐릭터 + 추가 스테이지 클리어 여부 + 누적 상자 목록을 보낸다.
	// 서버가 경험치를 가산하고 상자 보상을 검증·지급한다(서버 권위). cleared=false 는 실패(로그용, 지급 없음).
	[System.Serializable]
	public class DungeonClearRequest
	{
		public int dungeonId;
		public int characterId;
		public bool cleared;
		public bool extraCleared;
		public RewardBoxDto[] boxes;
	}

	// exp = 보상 가산 후 전투 캐릭터의 누적 경험치(권위값). rewards = 서버가 확정한 실제 획득 목록.
	[System.Serializable]
	public class DungeonClearResponse : ServerResponse
	{
		public int exp;
		public GrantedRewardDto[] rewards;
	}
}
