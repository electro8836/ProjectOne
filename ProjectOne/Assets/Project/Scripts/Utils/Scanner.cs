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

			// 대상 원이 부채꼴 꼭짓점을 품으면 방향과 무관하게 명중 — 시전자와 겹쳐 있는 적이 빠지지 않게 한다.
			if (sqrDist <= targetRadius * targetRadius)
			{
				return true;
			}

			// 완전히 같은 위치면 방향 계산 불가 → 명중 처리 (targetRadius 가 0 인 호출용 방어)
			if (sqrDist < 1E-06f)
			{
				return true;
			}

			float dist = Mathf.Sqrt(sqrDist);

			// 각도 여유 — 반지름 targetRadius 인 원은 거리 dist 에서 asin(targetRadius / dist) 만큼의
			// 각폭을 차지한다. 그만큼 넓혀야 "몸통은 걸쳤는데 중심이 각도 밖" 인 대상이 빠지지 않는다.
			// 거리 축이 이미 반경 합(reach)으로 판정하므로, 각도 축도 같은 의미로 맞추는 것이다.
			float limitRad = halfRad + Mathf.Asin(Mathf.Clamp01(targetRadius / dist));
			if (limitRad >= Mathf.PI)
			{
				return true;
			}

			// 대상 중심 방향 기준 각도 판정 (여유 포함)
			// dot(dir, val) = dist·cos(θ) → θ ≤ limit ⟺ dot(dir, val) ≥ cos(limit)·dist
			return Vector2.Dot(dir, val) >= Mathf.Cos(limitRad) * dist;
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
			// 근단도 원단과 대칭으로 대상 반지름만큼 여유를 둔다 — 중심이 살짝 뒤여도 몸통이 걸치면 명중이다.
			if (num < 0f - targetRadius || num > length + targetRadius)
			{
				return false;
			}

			float num3 = width * 0.5f + targetRadius;
			return Mathf.Abs(num2) <= num3;
		}
	}
}
