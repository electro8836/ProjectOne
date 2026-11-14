using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Map
{
	// 월드 씬의 맵 루트 — 이어진 타일맵 하나(TilemapGrid)와 그 위의 구역(ZoneArea)들.
	//
	// 월드는 씬에 직접 배치되므로 Addressable 로 띄우지 않는다. WorldDirector 가 씬 로드 뒤
	// MapManager.RegisterWorldMap 으로 등록한다. 씬의 MapContainer 아래에 두어야 씬 언로드가 곧 정리가 된다.
	[RequireComponent(typeof(TilemapGrid))]
	public class WorldMap : MonoBehaviour
	{
		[Tooltip("Table_World.ID")]
		[SerializeField] private int _worldId;

		private TilemapGrid _grid;
		private ZoneArea[] _zones;

		// 마지막으로 찾은 구역 — 히어로는 대개 한 구역 안에 머무르므로 캐시가 거의 항상 적중한다.
		private ZoneArea _lastHit;

		public int WorldId
		{
			get { return _worldId; }
		}

		public TilemapGrid Grid
		{
			get
			{
				ensureZones();
				return _grid;
			}
		}

		public IReadOnlyList<ZoneArea> Zones
		{
			get
			{
				ensureZones();
				return _zones;
			}
		}

		public ZoneArea GetZone(int zoneId)
		{
			ensureZones();
			for (int i = 0; i < _zones.Length; i++)
			{
				if (_zones[i].ZoneId == zoneId)
				{
					return _zones[i];
				}
			}

			return null;
		}

		// 좌표가 속한 구역 ID. 어느 구역에도 안 들어가면 0 (구역 사이 빈틈 등).
		public int FindZoneIdAt(Vector2 pos)
		{
			ensureZones();
			if (_lastHit != null && _lastHit.Contains(pos) == true)
			{
				return _lastHit.ZoneId;
			}

			for (int i = 0; i < _zones.Length; i++)
			{
				if (_zones[i].Contains(pos) == true)
				{
					_lastHit = _zones[i];
					return _zones[i].ZoneId;
				}
			}

			return 0;
		}

		private void ensureZones()
		{
			if (_zones != null)
			{
				return;
			}

			_grid = this.GetComponent<TilemapGrid>();
			_zones = this.GetComponentsInChildren<ZoneArea>(true);
		}
	}
}
