using UnityEngine;
using EDT;

namespace ProjectOne.Dungeon
{
	// Table_Stage.ModeType(MapModeType) 값을 구체 IStageMode 구현으로 매핑한다.
	public static class StageModeFactory
	{
		public static IStageMode Create(MapModeType mode)
		{
			switch (mode)
			{
			case MapModeType.Defense:
				return new DefenseStageMode();
			case MapModeType.BossBattle:
				return new BossBattleStageMode();
			default:
				Debug.LogWarning($"[StageModeFactory] 미구현 모드 {mode} — 즉시 클리어 스텁으로 진행");
				return new StubStageMode();
			}
		}
	}
}
