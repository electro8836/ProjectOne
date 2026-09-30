using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Map;

namespace ProjectOne.Dungeon
{
	// 유적 던전 맵의 기믹 묶음 — 맵 프리팹 루트(TilemapGrid 와 같은 오브젝트)에 붙인다.
	//
	// 코어 자리·문·포탈·공격 영역·상자 자리는 인스펙터로 직접 연결하고,
	// 개수가 맵마다 다른 공격 지점은 자식에서 모은다. 시작 지점은 기존 MapAnchor(Entry)를 쓴다.
	// 상자는 맵에 두지 않는다 — 단계 테이블의 ChestGrade 외형으로 모드가 상자 자리에 생성한다.
	//
	// 스폰 그룹 규약 (RuinsDungeon.MonsterSpawnGroupIDs)
	//   [0] 코어      → _coreSlot
	//   [1] 방어장치  → _defenseSlots
	//   [2~] 페이즈 소환 → _summonSlots (RuinsCorePhase.SpawnGroupIndex 로 지목)
	public class RuinsMap : MonoBehaviour
	{
		[SerializeField] private DungeonSpawnSlot _coreSlot;
		[SerializeField] private DungeonSpawnSlot[] _defenseSlots;
		[SerializeField] private DungeonSpawnSlot[] _summonSlots;

		[Tooltip("상자 자리(위치만). 앞에서부터 단계의 ChestCount 개 자리에 상자가 생성된다")]
		[SerializeField] private Transform[] _chestSlots;

		[Tooltip("방어 시스템 공격 영역(전투방). 가로줄 공격이 이 높이를 등분한다")]
		[SerializeField] private BoxCollider2D _hazardArea;

		[Tooltip("보상방 입구를 막는 문 (MapBlocker). 코어가 파괴되면 꺼진다. 보상방이 없는 맵은 비워 둔다")]
		[SerializeField] private GameObject _door;

		[Tooltip("나가는 포탈 표시. 열쇠를 다 쓰면 켜진다")]
		[SerializeField] private GameObject _portal;

		// 포탈 진입 판정 구역. 씬 뷰에서 박스 핸들로 크기를 맞추는 편집 수단일 뿐이다.
		[SerializeField] private BoxCollider2D _portalArea;

		private RuinsHazardPoint[] _points;
		private MapAnchor _entry;

		public DungeonSpawnSlot CoreSlot
		{
			get { return _coreSlot; }
		}

		public IReadOnlyList<DungeonSpawnSlot> DefenseSlots
		{
			get { return _defenseSlots; }
		}

		public IReadOnlyList<DungeonSpawnSlot> SummonSlots
		{
			get { return _summonSlots; }
		}

		public IReadOnlyList<Transform> ChestSlots
		{
			get { return _chestSlots; }
		}

		public IReadOnlyList<RuinsHazardPoint> HazardPoints
		{
			get
			{
				ensureCollected();
				return _points;
			}
		}

		// 시작 지점. Entry 가 없으면 맵 루트 위치다.
		public Vector3 EntryPosition
		{
			get
			{
				ensureCollected();
				return (_entry != null) ? _entry.Position : this.transform.position;
			}
		}

		public bool GetHazardRect(out Vector2 min, out Vector2 max)
		{
			return BoxArea.GetWorldRect(_hazardArea, out min, out max);
		}

		// 문을 연다. 이동 차단은 TilemapGrid 캐시에서 나오므로 바꾼 뒤 다시 굽는다 (LabyrinthSpawnTrigger 와 같은 방식).
		public void OpenDoor()
		{
			if (_door == null || _door.activeSelf == false)
			{
				return;
			}

			_door.SetActive(false);

			TilemapGrid grid = this.GetComponent<TilemapGrid>();
			if (grid != null)
			{
				grid.RefreshBlockers();
			}
		}

		public void SetPortalVisible(bool visible)
		{
			if (_portal != null)
			{
				_portal.SetActive(visible);
			}
		}

		public bool IsInPortal(Vector2 pos)
		{
			return BoxArea.Contains(_portalArea, pos);
		}

		private void ensureCollected()
		{
			if (_points != null)
			{
				return;
			}

			_points = this.GetComponentsInChildren<RuinsHazardPoint>(true);
			_entry = this.GetComponentInChildren<MapAnchor>(true);
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			BoxArea.DrawGizmo(_hazardArea, new Color(1f, 0.3f, 0.3f));
			BoxArea.DrawGizmo(_portalArea, new Color(0.2f, 0.5f, 1f));

			if (_coreSlot != null)
			{
				Gizmos.color = Color.red;
				Gizmos.DrawWireSphere(_coreSlot.Position, 0.6f);
			}

			if (_chestSlots != null)
			{
				Gizmos.color = Color.cyan;
				for (int i = 0; i < _chestSlots.Length; i++)
				{
					if (_chestSlots[i] != null)
					{
						Gizmos.DrawWireCube(_chestSlots[i].position, new Vector3(0.5f, 0.5f, 0f));
					}
				}
			}
		}
#endif
	}
}
