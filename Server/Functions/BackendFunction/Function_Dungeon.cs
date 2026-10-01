using System.IO;

namespace BackendFunction
{
	// 던전 클리어 — 재작성 대기(stub).
	//
	// 예전 구현은 클라가 보낸 상자(RewardBoxDto)를 검증·추첨하고 USER_CHARACTER/USER_CARDSKILL 에 지급했다.
	// 지금 클라는 (dungeonType, stage, cleared) 만 보내고, 캐릭터·카드스킬 도메인은 없어졌으며,
	// 보상은 RewardGroupID(Reward/RewardItemPool/EquipGradeWeight) 체계로 바뀌었다.
	// 서버 보상 엔진을 새 체계로 옮긴 뒤 다시 작성한다.
	//
	// 그동안은 실패를 돌려준다 — 클라는 실패 응답에서 계정을 건드리지 않는다(경험치를 0 으로 덮지 않도록 success 를 주지 않는다).
	public class Dungeon
	{
		public Stream DungeonClear()
		{
			return FuncResult.Error("dungeon clear not implemented");
		}
	}
}
