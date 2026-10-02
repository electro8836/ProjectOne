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

	// 입장 — 서버가 해금·남은 횟수를 확인해 1회 차감하고 런(시드)을 발급한다. 실패·즉시 이탈도 환불 없다.
	[System.Serializable]
	public class DungeonEnterRequest
	{
		public int dungeonType;		// EDT.Dungeon 정수
		public int stage;			// 균열은 시작 웨이브(체크포인트)
	}

	[System.Serializable]
	public class DungeonEnterResponse : ServerResponse
	{
		public DungeonRunDto run;
		public DungeonEntryDto entry;	// 차감 후 진행도
	}

	// 균열 소탕 — 입장 1회를 쓰고 입장보상만 받는다.
	[System.Serializable]
	public class DungeonSweepRequest
	{
		public int dungeonType;
	}

	[System.Serializable]
	public class DungeonSweepResponse : ServerResponse
	{
		public GrantedRewardDto[] rewards;
		public DungeonEntryDto entry;
	}

	// 미궁에서 연 상자 1개 — 맵 배치 순서의 인덱스와 외형 등급. 상자 보상은 바닥에 드랍되고 주운 것만 지급된다.
	[System.Serializable]
	public class DungeonChestDto
	{
		public int chestIndex;
		public int grade;			// EDT.DungeonChestGrade 정수
		public long pickedMask;		// 주운 보상 인덱스 비트(런 시드 추첨 결과 순서 기준)
	}

	// 런 종료 정산 — 클리어·실패·강제 귀환 모두 보낸다. 클라는 "어느 런이 어떻게 끝났다"와 연 상자(미궁)만 보낸다.
	// 보상은 서버가 테이블 RewardGroupID 로 직접 굴리고, 상자는 런 시드로 재현한다(서버 권위).
	[System.Serializable]
	public class DungeonClearRequest
	{
		public int runId;
		public int dungeonType;		// EDT.Dungeon 정수
		public int stage;
		public bool cleared;
		public int masteryId;		// 클리어 시점 장착 무기의 마스터리(WeaponMastery, 0 = 미착용) — 경험치 적립 대상
		public int clearedWave;		// 균열 — 통과한 마지막 웨이브
		public DungeonChestDto[] chests;
	}

	// exp = 보상 가산 후 캐릭터의 누적 경험치(권위값).
	// rewards·equipments 는 **클리어 보상만** 담는다 — 처치·상자 드랍은 주울 때 클라가 이미 같은 값으로 지급했다.
	[System.Serializable]
	public class DungeonClearResponse : ServerResponse
	{
		public int exp;
		public GrantedRewardDto[] rewards;			// 스택 아이템·재화
		public EquipmentInstanceDto[] equipments;	// 장비 — 서버가 UID·등급·품질을 확정한 인스턴스
		public DungeonEntryDto entry;				// 정산 후 진행도(최고 단계)
	}
}
