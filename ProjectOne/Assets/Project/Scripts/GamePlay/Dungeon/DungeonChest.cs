using UnityEngine;
using UnityEngine.Rendering;
using EDT;

namespace ProjectOne.Dungeon
{
	// 던전 보상 상자 — 미궁·유적 등이 공용으로 쓴다.
	//
	// 상자는 외형 등급(일반/고급/최고급)만 안다. 등급별 변형 프리팹이 Prefab_DungeonChest_Normal/Advanced/Premium 이다.
	// 무엇이 나오는지는 각 모드가 단계 테이블로 정한다 — 미궁은 등급별 보상 그룹, 유적은 보상 그룹을 무작위 배정.
	// 미궁은 맵 프리팹에 직접 배치하고, 유적은 단계의 ChestGrade 변형을 모드가 상자 자리에 생성한다.
	// 맵은 입장마다 새로 생성되므로 열림 상태를 따로 되돌리지 않는다.
	public class DungeonChest : MonoBehaviour
	{
		// 유닛과 같은 레이어여야 Y 정렬이 서로 비교된다 (NpcUnit·MapBlocker 와 같다).
		private const string SORTING_LAYER = "GamePlay";

		// worldY → sortingOrder 정밀도. UnitAnimator._precision 과 같아야 유닛과 같은 축에서 비교된다.
		private const float SORT_PRECISION = 1000f;

		[SerializeField] private GameObject _closedVisual;
		[SerializeField] private GameObject _openedVisual;

		[Tooltip("이 반경 안에 히어로가 있으면 개봉 게이지가 찬다")]
		[SerializeField] private float _radius = 1f;

		[Tooltip("외형 등급. 미궁은 이 등급의 보상 그룹을 쓴다 (베이스 프리팹은 None)")]
		[SerializeField] private DungeonChestGrade _grade = DungeonChestGrade.None;

		private bool _isOpened;

		public bool IsOpened
		{
			get { return _isOpened; }
		}

		public float Radius
		{
			get { return _radius; }
		}

		public DungeonChestGrade Grade
		{
			get { return _grade; }
		}

		public Vector2 Position
		{
			get { return this.transform.position; }
		}

		private void Awake()
		{
			applyVisual();
		}

		// 켜지는 시점의 위치로 정렬한다 — 유적은 생성 후 자리로 옮기고 코어가 파괴될 때 켠다.
		// 상자는 움직이지 않으므로 1회로 충분하다.
		private void OnEnable()
		{
			applySorting();
		}

		public void Open()
		{
			_isOpened = true;
			applyVisual();
		}

		private void applyVisual()
		{
			if (_closedVisual != null)
			{
				_closedVisual.SetActive(_isOpened == false);
			}

			if (_openedVisual != null)
			{
				_openedVisual.SetActive(_isOpened == true);
			}
		}

		// 발밑 Y 로 sortingOrder 를 정해 히어로와 같은 축에서 앞뒤가 결정되게 한다 (NpcUnit.applySorting 과 같은 방식).
		// 그룹을 루트에 얹으면 자식 렌더러(Closed/Opened)의 상대 순서는 그대로 남는다.
		private void applySorting()
		{
			SortingGroup group = this.GetComponent<SortingGroup>();
			if (group == null)
			{
				group = this.gameObject.AddComponent<SortingGroup>();
			}

			// Y 가 클수록(위) 뒤로 → 음수
			group.sortingOrder = -Mathf.RoundToInt(this.transform.position.y * SORT_PRECISION);
			group.sortingLayerID = SortingLayer.NameToID(SORTING_LAYER);
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(this.transform.position, _radius);
		}
#endif
	}
}
