using UnityEngine;
using EDT;

namespace ProjectOne.Battle
{
	// Table_MapInfo.BattleType 값을 구체 IBattleMode 구현으로 매핑한다.
	public static class BattleModeFactory
	{
		public static IBattleMode Create(BattleType type)
		{
			switch (type)
			{
			case BattleType.Wave:
				return new WaveMode();
			case BattleType.Raid:
				return new RaidMode();
			default:
				Debug.LogError($"[BattleModeFactory] 지원하지 않는 BattleType: {type} — 웨이브 모드로 폴백");
				return new WaveMode();
			}
		}
	}
}
