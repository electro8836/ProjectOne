namespace ProjectOne.Battle
{
	// 전투 한 판의 결과 상태
	public enum BattleResult
	{
		InProgress,
		Victory,
		Defeat,
	}

	// Lobby → Battle 진입 파라미터. BattleState 생성자로 전달되어 BattleDirector 가 소비한다.
	// 진행 규칙(웨이브/레이드)은 Table_MapInfo.BattleType 으로 결정한다.
	public sealed class BattleContext
	{
		public int MapId;       // Table_MapInfo ID
		public int CharacterId; // Table_Character ID (단일 — 동료 없음)
	}
}
