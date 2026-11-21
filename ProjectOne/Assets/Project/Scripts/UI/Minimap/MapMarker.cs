using UnityEngine;

namespace ProjectOne.UI
{
	public enum MapMarkerKind
	{
		Npc,
		Boss,
		Gate,
		Custom
	}

	// 지도 위 표시 하나 — 미니맵과 월드맵이 같은 목록을 쓴다.
	// 필드보스는 살았는지와 남은 시간에 따라 Label·Dead 가 바뀐다(MapMarkerCollector.RefreshBosses).
	public sealed class MapMarker
	{
		public MapMarkerKind Kind;
		public Vector2 Position;
		public string Icon;
		public string Label;

		// 필드보스 전용 — 처치 기록의 키와 살아 있을 때 표시할 이름
		public int BossZoneId;
		public int BossSpawnId;
		public string BossName;
		public bool Dead;
	}
}
