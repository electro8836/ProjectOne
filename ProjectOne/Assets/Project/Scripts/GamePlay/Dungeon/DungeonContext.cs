namespace ProjectOne.Dungeon
{
	// 스테이지 1판의 진행 결과. 모드가 확정하고 DungeonDirector 가 폴링한다.
	public enum DungeonResult
	{
		InProgress,
		Cleared,
		Defeat,
	}

	// 던전 진입 파라미터. DungeonState → DungeonDirector 로 전달된다.
	// (스테이지 진행 인덱스 등 실행 중 상태는 DungeonDirector 내부에서 관리)
	public sealed class DungeonContext
	{
		public int DungeonId;
		public int CharacterId;
	}
}
