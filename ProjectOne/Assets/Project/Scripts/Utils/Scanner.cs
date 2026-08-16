using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Utils
{
	public static class Scanner
	{
		public static bool InCircle(Vector2 origin, float radius, Vector2 point, float targetRadius = 0f)
		{
			Vector2 val = point - origin;
			float num = radius + targetRadius;
			return val.sqrMagnitude <= num * num;
		}

		public static bool InSector(Vector2 origin, Vector2 facing, float radius, float fullAngleDeg, Vector2 point, float targetRadius = 0f)
		{
			Vector2 val = point - origin;
			float sqrDist = val.sqrMagnitude;
			float reach = radius + targetRadius;
			// 거리 초과 (대상 반지름까지 포함해도 사거리 밖)
			if (sqrDist > reach * reach)
			{
				return false;
			}

			// facing 정규화 방어 (InLine과 동일 패턴)
			Vector2 dir = facing;
			if (dir.sqrMagnitude < 1E-06f)
			{
				dir = Vector2.right;
			}
			else
			{
				dir.Normalize();
			}

			float halfRad = fullAngleDeg * 0.5f * Mathf.Deg2Rad;
			// 반각이 180° 이상이면 사거리 내 전방위 → 거리 통과만으로 명중
			if (halfRad >= Mathf.PI)
			{
				return true;
			}

			// 완전히 같은 위치면 방향 계산 불가 → 명중 처리
			if (sqrDist < 1E-06f)
			{
				return true;
			}

			// 대상 중심 방향 기준 각도 판정
			// dot(dir, val) = dist·cos(θ), cosHalf·√sqrDist = dist·cos(halfAngle)
			// → θ ≤ halfAngle ⟺ dot(dir, val) ≥ cosHalf·dist
			float cosHalf = Mathf.Cos(halfRad);
			return Vector2.Dot(dir, val) >= cosHalf * Mathf.Sqrt(sqrDist);
		}

		public static bool InLine(Vector2 origin, Vector2 facing, float length, float width, Vector2 point, float targetRadius = 0f)
		{
			Vector2 val = facing;
			if (val.sqrMagnitude < 1E-06f)
			{
				val = Vector2.right;
			}
			else
			{
				val.Normalize();
			}

			Vector2 val2 = new Vector2(val.y, 0f - val.x);
			Vector2 val3 = point - origin;
			float num = Vector2.Dot(val3, val);
			float num2 = Vector2.Dot(val3, val2);
			if (num < 0f || num > length + targetRadius)
			{
				return false;
			}

			float num3 = width * 0.5f + targetRadius;
			return Mathf.Abs(num2) <= num3;
		}
	}
}
