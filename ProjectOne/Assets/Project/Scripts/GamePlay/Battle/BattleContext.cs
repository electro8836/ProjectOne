namespace ProjectOne.Battle
{
	// 전투 진행 규칙(모드) — 보상 성격을 나타내는 EDT DungeonType 과는 다른 축이다.
	public enum BattleMode
	{
		NormalDungeon,
		TicketDungeon,
		Raid,
	}

	// 전투 한 판의 결과 상태
	public enum BattleResult
	{
		InProgress,
		Victory,
		Defeat,
	}

	// Lobby → Battle 진입 파라미터. BattleState 생성자로 전달되어 BattleDirector 가 소비한다.
	public sealed class BattleContext
	{
		public BattleMode Mode;
		public int DungeonId;            // Table_DungeonList ID
		public int Step;                 // Table_DungeonStage Step
		public int[] PartyCharacterIds;  // Table_Character ID 목록
	}
}
