using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Map
{
	// 월드 맵 안의 구역 하나 — 사각형 영역 + 그 안의 스폰·NPC·시작 지점 마커.
	//
	// 구역은 월드 타일맵 위의 관리 단위일 뿐이라 경계에 로딩도 벽도 없다. 히어로가 어느 구역에 있는지는
	// 좌표로 판정한다(WorldMap.FindZoneIdAt). _zoneId 는 Table_Zone.ID(= Map.ID)다.
	//
	// 마커는 이 오브젝트의 자식으로 둔다 — 구역 소속을 좌표 계산 없이 계층으로 정한다.
	// 몬스터는 히어로가 있는 구역과 _neighborZoneIds 구역에서만 활성화된다.
	//
	// Physics2D 를 쓰지 않는다. BoxCollider2D 는 씬 뷰에서 사각형을 저작하는 수단일 뿐이다 (MapBlocker 와 같은 규약).
	public class ZoneArea : MonoBehaviour
	{
		[Tooltip("Table_Zone.ID (= Map.ID)")]
		[SerializeField] private int _zoneId;

		[SerializeField] private BoxCollider2D _area;

		[Tooltip("히어로가 이 구역에 있을 때 몬스터를 함께 활성화할 인접 구역 (Table_Zone.ID)")]
		[SerializeField] private int[] _neighborZoneIds;

		private MonsterSpawnPoint[] _spawnPoints;
		private NpcSpawnPoint[] _npcPoints;
		private MapAnchor _anchor;
		private bool _markersReady;

		public int ZoneId
		{
			get { return _zoneId; }
		}

		public IReadOnlyList<int> NeighborZoneIds
		{
			get { return _neighborZoneIds; }
		}

		public IReadOnlyList<MonsterSpawnPoint> SpawnPoints
		{
			get
			{
				ensureMarkers();
				return _spawnPoints;
			}
		}

		public IReadOnlyList<NpcSpawnPoint> NpcPoints
		{
			get
			{
				ensureMarkers();
				return _npcPoints;
			}
		}

		// 구역 시작 / 부활 지점. 마커가 없으면 영역 중심이다.
		public Vector3 AnchorPosition
		{
			get
			{
				ensureMarkers();
				if (_anchor != null)
				{
					return _anchor.Position;
				}

				Vector2 min;
				Vector2 max;
				if (BoxArea.GetWorldRect(_area, out min, out max) == false)
				{
					return this.transform.position;
				}

				Vector2 center = (min + max) * 0.5f;
				return new Vector3(center.x, center.y, 0f);
			}
		}

		private void Reset()
		{
			_area = this.GetComponent<BoxCollider2D>();
		}

		public bool Contains(Vector2 pos)
		{
			return BoxArea.Contains(_area, pos);
		}

		private void ensureMarkers()
		{
			if (_markersReady == true)
			{
				return;
			}

			_spawnPoints = this.GetComponentsInChildren<MonsterSpawnPoint>(true);
			_npcPoints = this.GetComponentsInChildren<NpcSpawnPoint>(true);
			_anchor = this.GetComponentInChildren<MapAnchor>(true);
			_markersReady = true;
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			BoxArea.DrawGizmo(_area, Color.cyan);
		}
#endif
	}
}
