using UnityEngine;
using ProjectOne.Map;

namespace ProjectOne.Dungeon
{
	// 미궁 던전의 즉사 영역 — 시작 지점 아래에서 위로 일정 속도로 올라온다.
	//
	// 속도와 출발 지연은 단계 테이블(LabyrinthDungeon.FireSpeed/FireDelay)이 정하고,
	// 출발 위치와 크기는 맵 프리팹에 놓인 그대로다. 이동은 LabyrinthDungeonMode 가 직접 민다.
	public class LabyrinthFireWall : MonoBehaviour
	{
		// 즉사 범위. 씬 뷰에서 박스 핸들로 크기를 맞추는 편집 수단일 뿐이다.
		[SerializeField] private BoxCollider2D _area;

		// 영역 윗변의 월드 y. HUD 기믹 슬라이더가 이 값을 쓴다.
		public float TopY
		{
			get
			{
				Vector2 min;
				Vector2 max;
				if (BoxArea.GetWorldRect(_area, out min, out max) == false)
				{
					return this.transform.position.y;
				}

				return max.y;
			}
		}

		private void Reset()
		{
			_area = this.GetComponent<BoxCollider2D>();
		}

		public void Advance(float dy)
		{
			this.transform.position += new Vector3(0f, dy, 0f);
		}

		// 유닛 몸통(원)이 윗변에 조금이라도 닿으면 true
		public bool Overlaps(Vector2 center, float radius)
		{
			return BoxArea.OverlapsCircle(_area, center, radius);
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			BoxArea.DrawGizmo(_area, Color.red);
		}
#endif
	}
}
