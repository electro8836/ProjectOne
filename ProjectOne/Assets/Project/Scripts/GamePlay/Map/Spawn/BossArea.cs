using UnityEngine;

namespace ProjectOne.Map
{
	// 필드보스 구역 — 보스(DailyReset) 스폰 포인트와 같은 오브젝트에 붙인다.
	//
	// 히어로가 구역 안에 있으면 보스 배너가 뜨고 보스와 싸울 수 있다.
	// 구역 밖으로 나가면 배너가 사라지고 보스는 스폰 직후 상태로 초기화되며, 밖에서 넣는 피해는 무시된다.
	//
	// 모양은 BoxCollider2D / CircleCollider2D 로 저작한다. 여러 개를 두면 합친 영역이 된다(통로·ㄱ자 구역).
	// 콜라이더는 씬 뷰에서 모양을 맞추는 편집 수단일 뿐이다 — Physics2D 는 쓰지 않는다 (BoxArea 와 같은 규약).
	// 그래서 콜라이더는 꺼 둔다(물리 트리거로 다른 센서와 엮이지 않게). 꺼져 있어도 모양 값은 그대로 읽는다.
	// 회전은 지원하지 않는다.
	public class BossArea : MonoBehaviour
	{
		[Tooltip("구역 모양 — BoxCollider2D / CircleCollider2D. 여러 개면 합친 영역")]
		[SerializeField] private Collider2D[] _shapes;

		private void Reset()
		{
			_shapes = this.GetComponentsInChildren<Collider2D>();
		}

		public bool Contains(Vector2 pos)
		{
			if (_shapes == null)
			{
				return false;
			}

			for (int i = 0; i < _shapes.Length; i++)
			{
				if (shapeContains(_shapes[i], pos) == true)
				{
					return true;
				}
			}

			return false;
		}

		private static bool shapeContains(Collider2D shape, Vector2 pos)
		{
			BoxCollider2D box = shape as BoxCollider2D;
			if (box != null)
			{
				return BoxArea.Contains(box, pos);
			}

			CircleCollider2D circle = shape as CircleCollider2D;
			if (circle != null)
			{
				Vector2 center;
				float radius;
				getWorldCircle(circle, out center, out radius);
				return (pos - center).sqrMagnitude <= radius * radius;
			}

			return false;
		}

		private static void getWorldCircle(CircleCollider2D circle, out Vector2 center, out float radius)
		{
			Transform tr = circle.transform;
			Vector3 c = tr.TransformPoint(new Vector3(circle.offset.x, circle.offset.y, 0f));
			Vector3 scale = tr.lossyScale;
			center = new Vector2(c.x, c.y);
			radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
		}

#if UNITY_EDITOR
		private static readonly Color GizmoColor = new Color(1f, 0.35f, 0.3f);

		private void OnDrawGizmos()
		{
			if (_shapes == null)
			{
				return;
			}

			for (int i = 0; i < _shapes.Length; i++)
			{
				BoxCollider2D box = _shapes[i] as BoxCollider2D;
				if (box != null)
				{
					BoxArea.DrawGizmo(box, GizmoColor);
					continue;
				}

				CircleCollider2D circle = _shapes[i] as CircleCollider2D;
				if (circle != null)
				{
					Vector2 center;
					float radius;
					getWorldCircle(circle, out center, out radius);
					UnityEditor.Handles.color = GizmoColor;
					UnityEditor.Handles.DrawWireDisc(center, Vector3.forward, radius);
				}
			}
		}
#endif
	}
}
