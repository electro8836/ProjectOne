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
			float num = radius + targetRadius;
			if (val.sqrMagnitude > num * num)
			{
				return false;
			}
			if (val.sqrMagnitude < 1E-06f)
			{
				return true;
			}
			float num2 = fullAngleDeg * 0.5f;
			return Vector2.Angle(facing, val) <= num2;
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

		public static bool InDonut(Vector2 origin, float outerRadius, float innerRadius, Vector2 point, float targetRadius = 0f)
		{
			Vector2 val = point - origin;
			float sqrMagnitude = val.sqrMagnitude;
			float num = outerRadius + targetRadius;
			float num2 = Mathf.Max(0f, innerRadius - targetRadius);
			if (sqrMagnitude <= num * num)
			{
				return sqrMagnitude >= num2 * num2;
			}
			return false;
		}
	}
}
