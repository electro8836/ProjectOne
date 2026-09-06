using EDT;

namespace ProjectOne.Dungeon
{
	// 스테이지 1판의 진행 결과. 모드가 확정하고 DungeonDirector 가 폴링한다.
	public enum DungeonResult
	{
		InProgress,
		Cleared,
		Defeat,

		// 부활로 되돌릴 수 없는 실패(제한시간 초과). Defeat 와 달리 부활 팝업을 거치지 않는다.
		Failed,
	}

	// 결과창에서 플레이어가 고른 다음 행동. 자동 복귀 타임아웃도 ReturnTown 이다.
	public enum DungeonResultAction
	{
		ReturnTown,

		// 같은 단계 재입장
		Retry,

		// 다음 단계 입장
		NextStage,
	}

	// 던전 진입 파라미터. DungeonState → DungeonDirector 로 전달된다.
	//
	// 1회 입장 = DungeonStage 1단계다 (맵 설계 9장). 여러 단계를 연달아 진행하지 않는다.
	// 클리어 후 "다음 단계 도전"을 고르면 입장 횟수를 다시 소모하고 Stage + 1 로 새로 들어온다.
	public sealed class DungeonContext
	{
		public EDT.Dungeon DungeonType;
		public int Stage;

		public DungeonContext(EDT.Dungeon dungeonType, int stage)
		{
			DungeonType = dungeonType;
			Stage = stage > 0 ? stage : 1;
		}

		// (DungeonType, Stage) 로 실제 단계 행을 찾는다. 없으면 null.
		public Table_DungeonStage.Row FindStageRow()
		{
			return DungeonProgress.FindStageRow(DungeonType, Stage);
		}
	}
}
