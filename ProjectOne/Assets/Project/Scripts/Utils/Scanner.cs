using UnityEngine;

namespace ProjectOne.Utils
{
	// 2D 탑다운 기하 판정 헬퍼 — 광역 탐지(스킬, AI 시야, 트리거 등)에서 공용
	public static class Scanner
	{
		// 원형 판정 — point 가 origin 중심 반지름 radius 원 안에 있는가
		public static bool InCircle(Vector2 origin, float radius, Vector2 point)
		{
			Vector2 rel = point - origin;
			return rel.sqrMagnitude <= radius * radius;
		}

		// 부채꼴 판정 — facing 방향 기준 fullAngleDeg 전체각(좌우 절반씩) 부채꼴 안에 있는가
		public static bool InSector(Vector2 origin, Vector2 facing, float radius, float fullAngleDeg, Vector2 point)
		{
			Vector2 rel = point - origin;
			if (rel.sqrMagnitude > radius * radius)
			{
				return false;
			}
			if (rel.sqrMagnitude < 1e-6f)
			{
				return true;
			}
			float half = fullAngleDeg * 0.5f;
			float angle = Vector2.Angle(facing, rel);
			return angle <= half;
		}

		// 직사각형 판정 — facing 방향 기준 origin 에서 시작해 length 만큼 뻗는 width 너비 사각형
		public static bool InLine(Vector2 origin, Vector2 facing, float length, float width, Vector2 point)
		{
			Vector2 forward = facing;
			if (forward.sqrMagnitude < 1e-6f)
			{
				forward = Vector2.right;
			}
			else
			{
				forward.Normalize();
			}
			Vector2 right = new Vector2(forward.y, -forward.x);

			Vector2 rel = point - origin;
			float localX = Vector2.Dot(rel, forward);
			float localY = Vector2.Dot(rel, right);

			if (localX < 0f || localX > length)
			{
				return false;
			}
			float halfW = width * 0.5f;
			return Mathf.Abs(localY) <= halfW;
		}

		// 도넛 판정 — 외경 outerRadius, 내경 innerRadius 사이 링 안에 있는가
		public static bool InDonut(Vector2 origin, float outerRadius, float innerRadius, Vector2 point)
		{
			Vector2 rel = point - origin;
			float distSqr = rel.sqrMagnitude;
			float outerSqr = outerRadius * outerRadius;
			float innerSqr = innerRadius * innerRadius;
			return distSqr <= outerSqr && distSqr >= innerSqr;
		}
	}
}
