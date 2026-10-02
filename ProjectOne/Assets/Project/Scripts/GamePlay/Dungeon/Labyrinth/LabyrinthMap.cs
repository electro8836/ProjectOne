using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Map;

namespace ProjectOne.Dungeon
{
	// 미궁 던전 맵의 기믹 묶음 — 맵 프리팹 루트(TilemapGrid 와 같은 오브젝트)에 붙인다.
	//
	// 출구와 불은 하나뿐이라 인스펙터로 직접 연결하고, 개수가 맵마다 다른 트리거·상자는 자식에서 모은다.
	// 시작 지점은 기존 MapAnchor(Entry)를 그대로 쓴다.
	public class LabyrinthMap : MonoBehaviour
	{
		// 도착 구역 — 기관장치 상호작용 범위다. 씬 뷰에서 박스 핸들로 크기를 맞추는 편집 수단일 뿐이다.
		// 기관장치 외형은 이 구역 안에 배치한다(코드는 외형을 보지 않는다).
		[SerializeField] private BoxCollider2D _exitArea;
		[SerializeField] private LabyrinthFireWall _fireWall;

		// 기관장치를 작동하면 되돌아가는 길을 막는 장막. 비어 있으면 막지 않는다.
		[SerializeField] private GameObject _exitCurtain;

		private LabyrinthSpawnTrigger[] _triggers;
		private DungeonChest[] _chests;
		private MapAnchor _entry;

		public LabyrinthFireWall FireWall
		{
			get { return _fireWall; }
		}

		// Order 오름차순
		public IReadOnlyList<LabyrinthSpawnTrigger> Triggers
		{
			get
			{
				ensureCollected();
				return _triggers;
			}
		}

		public IReadOnlyList<DungeonChest> Chests
		{
			get
			{
				ensureCollected();
				return _chests;
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

		// 도착 구역 중심의 월드 y. 진행도 슬라이더의 1 이다.
		public float ExitY
		{
			get
			{
				Vector2 min;
				Vector2 max;
				if (BoxArea.GetWorldRect(_exitArea, out min, out max) == false)
				{
					return this.transform.position.y;
				}

				return (min.y + max.y) * 0.5f;
			}
		}

		public bool IsInExit(Vector2 pos)
		{
			return BoxArea.Contains(_exitArea, pos);
		}

		public void SetExitCurtain(bool active)
		{
			if (_exitCurtain != null)
			{
				_exitCurtain.SetActive(active);
			}
		}

		private void ensureCollected()
		{
			if (_triggers != null)
			{
				return;
			}

			_triggers = this.GetComponentsInChildren<LabyrinthSpawnTrigger>(true);
			_chests = this.GetComponentsInChildren<DungeonChest>(true);
			_entry = this.GetComponentInChildren<MapAnchor>(true);

			System.Array.Sort(_triggers, compareOrder);
		}

		private static int compareOrder(LabyrinthSpawnTrigger a, LabyrinthSpawnTrigger b)
		{
			return a.Order.CompareTo(b.Order);
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			BoxArea.DrawGizmo(_exitArea, Color.green);
		}
#endif
	}
}
