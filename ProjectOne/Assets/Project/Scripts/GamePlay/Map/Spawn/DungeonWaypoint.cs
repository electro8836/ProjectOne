using UnityEngine;

namespace ProjectOne.Map
{
	// 던전 웨이포인트 — 균열 던전 몬스터의 이동 경로 지점. 그리드맵 프리팹에 배치한다.
	//
	// 몬스터는 스폰 슬롯에서 출발해 Index 오름차순으로 지점을 밟고, **마지막 지점이 도착지점**이다.
	// 경로의 꺾임(오른쪽→위→왼쪽…)은 지점 배치가 정한다 — 코드는 순서만 안다.
	public class DungeonWaypoint : MonoBehaviour
	{
		[Tooltip("경로 순서. 작은 번호부터 밟는다")]
		[SerializeField] private int _index;

		public int Index
		{
			get { return _index; }
		}

		public Vector3 Position
		{
			get { return this.transform.position; }
		}

#if UNITY_EDITOR
		// 스폰 슬롯(주황)과 구분되어야 한다.
		private static readonly Color GizmoColor = new Color(0.2f, 0.8f, 1f);

		// 도착 판정 반경과 무관한 표시용 크기
		private const float GizmoRadius = 0.3f;

		private void OnDrawGizmos()
		{
			bool selected = UnityEditor.Selection.Contains(this.gameObject);
			SpawnRadiusGizmo.Draw(this.transform, GizmoRadius, GizmoColor, selected, "경로 " + _index);
		}
#endif
	}
}
