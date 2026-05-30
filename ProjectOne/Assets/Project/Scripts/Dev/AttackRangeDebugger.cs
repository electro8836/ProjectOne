using System;
using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.Skill;

namespace ProjectOne.Dev
{
	[DisallowMultipleComponent]
	public class AttackRangeDebugger : MonoBehaviour
	{
		private struct ActiveScan
		{
			public SkillScanType type;

			public float p1;

			public float p2;

			public Vector2 origin;

			public Vector2 facing;

			public float startTime;
		}

		[SerializeField]
		private bool _show = true;

		[SerializeField]
		private float _alpha = 0.6f;

		[SerializeField]
		private float _duration = 0.5f;

		private readonly List<ActiveScan> _active = new List<ActiveScan>(8);

		private static readonly Color[] _palette = new Color[5]
		{
			new Color(0.2f, 1f, 0.4f),
			new Color(1f, 0.8f, 0.1f),
			new Color(0.3f, 0.7f, 1f),
			new Color(1f, 0.3f, 0.5f),
			new Color(0.9f, 0.4f, 1f)
		};

		private const int CircleSegments = 32;

		private const int ArcSegments = 32;

		private void OnEnable()
		{
			Singleton<EventManager>.Instance.Subscribe<SkillCastEvent>(onSkillCast);
		}

		private void OnDisable()
		{
			Singleton<EventManager>.Instance.Unsubscribe<SkillCastEvent>(onSkillCast);
		}

		private void onSkillCast(SkillCastEvent e)
		{
			if (e.Caster == null || e.Caster.GetUnitType() != UnitType.Hero)
			{
				return;
			}
			Table_SkillInfo.Row row = Table_SkillInfo.Get(e.SkillId);
			if (row == null || !isAreaScan(row.ScanType))
			{
				return;
			}
			Vector2 facing = Vector2.right;
			UnitMover component = e.Caster.GetComponent<UnitMover>();
			if (component != null)
			{
				Vector2 facing2 = component.Facing;
				if (facing2.sqrMagnitude > 1E-06f)
				{
					facing = component.Facing;
				}
			}
			ActiveScan activeScan = default(ActiveScan);
			activeScan.type = row.ScanType;
			activeScan.p1 = row.ScanParam1;
			activeScan.p2 = row.ScanParam2;
			activeScan.origin = e.Caster.HitCenter;
			activeScan.facing = facing;
			activeScan.startTime = Time.time;
			ActiveScan item = activeScan;
			_active.Add(item);
		}

		private static bool isAreaScan(SkillScanType type)
		{
			if (type != SkillScanType.Circle && type != SkillScanType.Sector && type != SkillScanType.Line)
			{
				return type == SkillScanType.Donut;
			}
			return true;
		}

		private void OnDrawGizmos()
		{
			if (!_show)
			{
				return;
			}
			for (int num = _active.Count - 1; num >= 0; num--)
			{
				ActiveScan activeScan = _active[num];
				if (Time.time - activeScan.startTime > _duration)
				{
					int index = _active.Count - 1;
					_active[num] = _active[index];
					_active.RemoveAt(index);
				}
				else
				{
					Color color = _palette[num % _palette.Length];
					color.a = _alpha;
					Gizmos.color = color;
					drawScan(activeScan.origin, activeScan.facing, activeScan.type, activeScan.p1, activeScan.p2);
				}
			}
		}

		private void drawScan(Vector2 origin, Vector2 facing, SkillScanType type, float p1, float p2)
		{
			switch (type)
			{
			case SkillScanType.Circle:
				drawCircle(origin, p1);
				break;
			case SkillScanType.Sector:
				drawSector(origin, facing, p1, p2);
				break;
			case SkillScanType.Line:
				drawRect(origin, facing, p1, p2);
				break;
			case SkillScanType.Donut:
				drawCircle(origin, p1);
				drawCircle(origin, p2);
				break;
			}
		}

		private void drawCircle(Vector2 center, float radius)
		{
			float num = 11.25f;
			Vector3 val2 = default(Vector3);
			for (int i = 0; i < 32; i++)
			{
				float num2 = (float)i * num * (MathF.PI / 180f);
				float num3 = (float)(i + 1) * num * (MathF.PI / 180f);
				Vector3 val = new Vector3(center.x + Mathf.Cos(num2) * radius, center.y + Mathf.Sin(num2) * radius, 0f);
				val2 = new Vector3(center.x + Mathf.Cos(num3) * radius, center.y + Mathf.Sin(num3) * radius, 0f);
				Gizmos.DrawLine(val, val2);
			}
		}

		private void drawSector(Vector2 center, Vector2 facing, float radius, float fullAngleDeg)
		{
			float num = Mathf.Atan2(facing.y, facing.x) * 57.29578f;
			float num2 = fullAngleDeg * 0.5f;
			float num3 = num - num2;
			float num4 = fullAngleDeg / 32f;
			Vector3 val2 = default(Vector3);
			for (int i = 0; i < 32; i++)
			{
				float num5 = (num3 + (float)i * num4) * (MathF.PI / 180f);
				float num6 = (num3 + (float)(i + 1) * num4) * (MathF.PI / 180f);
				Vector3 val = new Vector3(center.x + Mathf.Cos(num5) * radius, center.y + Mathf.Sin(num5) * radius, 0f);
				val2 = new Vector3(center.x + Mathf.Cos(num6) * radius, center.y + Mathf.Sin(num6) * radius, 0f);
				Gizmos.DrawLine(val, val2);
			}
			float num7 = num3 * (MathF.PI / 180f);
			float num8 = (num3 + fullAngleDeg) * (MathF.PI / 180f);
			Vector3 val3 = new Vector3(center.x, center.y, 0f);
			Gizmos.DrawLine(val3, new Vector3(center.x + Mathf.Cos(num7) * radius, center.y + Mathf.Sin(num7) * radius, 0f));
			Gizmos.DrawLine(val3, new Vector3(center.x + Mathf.Cos(num8) * radius, center.y + Mathf.Sin(num8) * radius, 0f));
		}

		private void drawRect(Vector2 origin, Vector2 facing, float length, float width)
		{
			Vector2 val = ((facing.sqrMagnitude > 1E-06f) ? facing.normalized : Vector2.right);
			Vector2 val2 = default(Vector2);
			val2 = new Vector2(val.y, 0f - val.x);
			float num = width * 0.5f;
			Vector2 val3 = origin + val * length - val2 * num;
			Vector2 val4 = origin + val * length + val2 * num;
			Vector2 val5 = origin - val2 * num;
			Vector2 val6 = origin + val2 * num;
			Gizmos.DrawLine(new Vector3(val5.x, val5.y, 0f), new Vector3(val6.x, val6.y, 0f));
			Gizmos.DrawLine(new Vector3(val5.x, val5.y, 0f), new Vector3(val3.x, val3.y, 0f));
			Gizmos.DrawLine(new Vector3(val6.x, val6.y, 0f), new Vector3(val4.x, val4.y, 0f));
			Gizmos.DrawLine(new Vector3(val3.x, val3.y, 0f), new Vector3(val4.x, val4.y, 0f));
		}
	}
}
