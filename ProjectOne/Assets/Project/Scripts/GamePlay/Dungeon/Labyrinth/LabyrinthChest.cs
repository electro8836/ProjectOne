using UnityEngine;

namespace ProjectOne.Dungeon
{
	// 미궁 던전 보상 상자 — 맵 프리팹에 직접 배치한다(중첩 프리팹 Prefab_LabyrinthChest).
	//
	// 위치와 개수는 맵마다 다르고, 보상 내용은 단계 테이블(ChestRewardGroupID)이 정한다.
	// 맵은 입장마다 새로 생성되므로 열림 상태를 따로 되돌리지 않는다.
	public class LabyrinthChest : MonoBehaviour
	{
		[SerializeField] private GameObject _closedVisual;
		[SerializeField] private GameObject _openedVisual;

		[Tooltip("이 반경 안에 히어로가 있으면 개봉 게이지가 찬다")]
		[SerializeField] private float _radius = 1f;

		private bool _isOpened;

		public bool IsOpened
		{
			get { return _isOpened; }
		}

		public float Radius
		{
			get { return _radius; }
		}

		public Vector2 Position
		{
			get { return this.transform.position; }
		}

		private void Awake()
		{
			applyVisual();
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

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(this.transform.position, _radius);
		}
#endif
	}
}
