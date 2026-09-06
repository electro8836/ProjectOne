using EDT;
using UnityEngine;

namespace ProjectOne.Dungeon
{
	// DungeonType → 구체 모드 매핑.
	//
	// 조합으로 표현하려 들면 던전이 늘어날 때마다 조합으로 안 되는 게 나와 결국 코드 분기를 추가하게 된다.
	// enum 하나가 곧 규칙 하나인 것이 정직하다 (맵 설계 9장).
	public static class StageModeFactory
	{
		public static IStageMode Create(EDT.Dungeon dungeonType)
		{
			switch (dungeonType)
			{
				case EDT.Dungeon.Gold:
					return new GoldDungeonMode();
				case EDT.Dungeon.Rift:
					return new RiftDungeonMode();
				default:
					Debug.LogError($"[StageModeFactory] 대응하는 모드가 없는 던전 종류: {dungeonType}");
					return null;
			}
		}
	}
}
