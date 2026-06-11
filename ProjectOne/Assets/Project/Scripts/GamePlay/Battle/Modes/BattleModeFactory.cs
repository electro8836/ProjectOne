using UnityEngine;

namespace ProjectOne.Battle
{
	// BattleMode 값을 구체 IBattleMode 구현으로 매핑한다.
	public static class BattleModeFactory
	{
		public static IBattleMode Create(BattleMode mode)
		{
			switch (mode)
			{
			case BattleMode.NormalDungeon:
				return new NormalDungeonMode();
			case BattleMode.TicketDungeon:
				return new TicketDungeonMode();
			case BattleMode.Raid:
				return new RaidMode();
			default:
				Debug.LogError($"[BattleModeFactory] 지원하지 않는 BattleMode: {mode} — 일반 던전으로 폴백");
				return new NormalDungeonMode();
			}
		}
	}
}
