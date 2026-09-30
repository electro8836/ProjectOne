using UnityEngine;

namespace ProjectOne.Dungeon
{
	// 유적 방어 시스템의 지점형 공격(원형·도넛) 기준점 — 맵 프리팹에 배치한다.
	//
	// 어떤 공격이 어느 지점에 떨어질지는 RuinsHazard.PointGroup 이 정한다(0 이면 모든 지점).
	// 지점은 위치와 그룹만 안다 — 크기·예고 시간은 테이블 값이다.
	public class RuinsHazardPoint : MonoBehaviour
	{
		[Tooltip("RuinsHazard.PointGroup 과 맞추는 그룹 번호(1부터)")]
		[SerializeField] private int _group = 1;

		public int Group
		{
			get { return _group; }
		}

		public Vector2 Position
		{
			get { return this.transform.position; }
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			Gizmos.color = (_group == 1) ? new Color(1f, 0.4f, 0.1f) : new Color(1f, 0.1f, 0.6f);
			Gizmos.DrawWireSphere(this.transform.position, 0.3f);
			UnityEditor.Handles.Label(this.transform.position, "H" + _group.ToString());
		}
#endif
	}
}
