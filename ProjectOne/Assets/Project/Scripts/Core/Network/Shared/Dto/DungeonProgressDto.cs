using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 던전 진행도 (USER_DUNGEON 저장 + GetUserData 응답). 서버가 소유하고 클라 DungeonProgress 가 반영한다.
	//
	// 입장(DungeonEnter)마다 서버가 run 을 새로 만들고 시드를 발급한다. 런 안의 상자는 이 시드로 굴리고,
	// 종료 정산(DungeonClear)에서 서버가 같은 시드로 재현해 지급한다. 진행 중인 런은 하나뿐이다.
	[System.Serializable]
	public class DungeonProgressDto
	{
		public List<DungeonEntryDto> entries = new List<DungeonEntryDto>();

		// 지금까지 발급한 런 수 — 런 식별자이자 상자 장비 UID 대역이다.
		public int runCounter;

		public DungeonRunDto run;
	}

	// 던전 하나의 진행도. usedToday 는 resetDay 가 오늘이 아니면 0 으로 본다(KST 06:00 경계).
	[System.Serializable]
	public class DungeonEntryDto
	{
		public int dungeonType;		// EDT.Dungeon 정수
		public int highestStage;	// 클리어한 최고 단계(균열은 최고 기록 웨이브). 영구 누적
		public int usedToday;
		public int resetDay;		// usedToday 를 센 날(ResetDay)
	}

	// 진행 중인(또는 마지막) 런. settled 면 정산이 끝나 더 받지 않는다.
	[System.Serializable]
	public class DungeonRunDto
	{
		public int runId;			// = 발급 당시 runCounter
		public long seed;
		public int dungeonType;
		public int stage;			// 균열은 시작 웨이브
		public long startUnixMs;
		public bool settled;
		public int reviveCount;		// 이번 런에서 쓴 유료 부활 횟수(DungeonRevive)
	}
}
