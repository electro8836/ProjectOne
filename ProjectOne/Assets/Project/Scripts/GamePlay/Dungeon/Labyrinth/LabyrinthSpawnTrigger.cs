using UnityEngine;
using ProjectOne.Map;

namespace ProjectOne.Dungeon
{
	// 미궁 던전 몬스터 스폰 트리거 — 히어로가 처음 들어오는 순간 1회 발동한다.
	//
	// 무엇이 나올지는 단계 테이블이 정한다: LabyrinthDungeon.MonsterSpawnGroupIDs[Order - 1].
	// 트리거는 순서와 "어느 슬롯에 내보낼지"만 안다 — 던전 슬롯과 같은 익명 규약이다.
	//
	// 발동하면 장막이 앞길을 막고, 이 구간 몬스터를 모두 처치하면 걷힌다 (LabyrinthDungeonMode 가 켜고 끈다).
	public class LabyrinthSpawnTrigger : MonoBehaviour
	{
		[Tooltip("발동 순서(1부터). MonsterSpawnGroupIDs 의 인덱스 + 1 이다")]
		[SerializeField] private int _order = 1;

		// 발동 구역. 씬 뷰에서 박스 핸들로 크기를 맞추는 편집 수단일 뿐이다.
		[SerializeField] private BoxCollider2D _area;

		[Tooltip("이 트리거가 몬스터를 내보낼 슬롯들. 몬스터가 슬롯보다 많으면 순환한다")]
		[SerializeField] private DungeonSpawnSlot[] _slots;

		[Tooltip("발동 중 다음 구간으로 가는 통로를 막는 장막 (MapBlocker). 평소에는 꺼 둔다")]
		[SerializeField] private GameObject _curtain;

		public int Order
		{
			get { return _order; }
		}

		public DungeonSpawnSlot[] Slots
		{
			get { return _slots; }
		}

		private void Reset()
		{
			_area = this.GetComponent<BoxCollider2D>();
		}

		public bool Contains(Vector2 pos)
		{
			return BoxArea.Contains(_area, pos);
		}

		// 장막을 켜고 끈다. 이동 차단은 TilemapGrid 캐시에서 나오므로 바꾼 뒤 다시 굽는다 (MapPortal 과 같은 방식).
		public void SetCurtain(bool active)
		{
			if (_curtain == null || _curtain.activeSelf == active)
			{
				return;
			}

			_curtain.SetActive(active);

			TilemapGrid grid = this.GetComponentInParent<TilemapGrid>();
			if (grid != null)
			{
				grid.RefreshBlockers();
			}
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			BoxArea.DrawGizmo(_area, Color.yellow);

			if (_curtain != null)
			{
				Gizmos.color = Color.magenta;
				Gizmos.DrawLine(this.transform.position, _curtain.transform.position);

				// 장막은 꺼진 채 저장되므로 씬 뷰에서 막히는 자리가 보이도록 영역을 항상 그린다.
				BoxCollider2D curtainBox = _curtain.GetComponent<BoxCollider2D>();
				Vector2 min;
				Vector2 max;
				if (BoxArea.GetWorldRect(curtainBox, out min, out max) == true)
				{
					Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
					Gizmos.DrawCube((min + max) * 0.5f, max - min);
				}

				BoxArea.DrawGizmo(curtainBox, Color.magenta);
			}

			if (_slots == null)
			{
				return;
			}

			// 어느 슬롯을 깨우는지 한눈에 보이게 선으로 잇는다.
			Gizmos.color = Color.yellow;
			for (int i = 0; i < _slots.Length; i++)
			{
				if (_slots[i] != null)
				{
					Gizmos.DrawLine(this.transform.position, _slots[i].Position);
				}
			}
		}
#endif
	}
}
