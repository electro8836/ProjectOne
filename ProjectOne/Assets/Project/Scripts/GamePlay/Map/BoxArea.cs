using UnityEngine;

namespace ProjectOne.Map
{
	// BoxCollider2D 로 저작한 사각형 영역의 월드 AABB 판정.
	//
	// 콜라이더는 씬 뷰에서 크기를 맞추는 편집 수단일 뿐이다 — Physics2D 는 쓰지 않는다 (MapPortal 과 같은 규약).
	// 회전은 지원하지 않는다.
	public static class BoxArea
	{
		public static bool Contains(BoxCollider2D area, Vector2 pos)
		{
			Vector2 min;
			Vector2 max;
			if (GetWorldRect(area, out min, out max) == false)
			{
				return false;
			}

			return pos.x >= min.x && pos.x <= max.x && pos.y >= min.y && pos.y <= max.y;
		}

		// 원(유닛 몸통)이 영역에 조금이라도 걸치는지 — 사각형 위 최근접점과 원 중심의 거리로 본다.
		public static bool OverlapsCircle(BoxCollider2D area, Vector2 center, float radius)
		{
			Vector2 min;
			Vector2 max;
			if (GetWorldRect(area, out min, out max) == false)
			{
				return false;
			}

			Vector2 closest = new Vector2(Mathf.Clamp(center.x, min.x, max.x), Mathf.Clamp(center.y, min.y, max.y));
			return (center - closest).sqrMagnitude <= radius * radius;
		}

		public static bool GetWorldRect(BoxCollider2D area, out Vector2 min, out Vector2 max)
		{
			if (area == null)
			{
				min = Vector2.zero;
				max = Vector2.zero;
				return false;
			}

			Transform tr = area.transform;
			Vector3 center = tr.TransformPoint(new Vector3(area.offset.x, area.offset.y, 0f));
			Vector3 scale = tr.lossyScale;
			float halfX = Mathf.Abs(area.size.x * scale.x) * 0.5f;
			float halfY = Mathf.Abs(area.size.y * scale.y) * 0.5f;

			min = new Vector2(center.x - halfX, center.y - halfY);
			max = new Vector2(center.x + halfX, center.y + halfY);
			return true;
		}

#if UNITY_EDITOR
		// 영역 외곽선 — 저작용 기즈모
		public static void DrawGizmo(BoxCollider2D area, Color color)
		{
			Vector2 min;
			Vector2 max;
			if (GetWorldRect(area, out min, out max) == false)
			{
				return;
			}

			Gizmos.color = color;
			Gizmos.DrawWireCube((min + max) * 0.5f, max - min);
		}
#endif
	}
}
