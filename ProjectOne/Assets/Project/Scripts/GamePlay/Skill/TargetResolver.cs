using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Unit;
using ProjectOne.Combat;

namespace ProjectOne.Skill
{
	public static class TargetResolver
	{
		private static readonly List<UnitBase> _scratch = new List<UnitBase>(32);

		private static readonly List<UnitBase> _filtered = new List<UnitBase>(32);

		private static readonly List<UnitBase> _self = new List<UnitBase>(1);

		public static List<UnitBase> ScanByType(SkillScanType scanType, float param1, float param2, UnitBase caster)
		{
			_scratch.Clear();
			if (caster == null)
			{
				return _scratch;
			}

			switch (scanType)
			{
			case SkillScanType.None:
				return _scratch;
			case SkillScanType.Chain:
				Debug.Log("[TargetResolver] Chain TODO — 빈 결과 반환");
				return _scratch;
			default:
			{
				Vector2 hitCenter = caster.HitCenter;
				Vector2 facing = GetFacing(caster);
				IReadOnlyList<UnitBase> all = UnitContainer.Instance.All;
				if (scanType == SkillScanType.Target)
				{
					UnitBase unitBase = null;
					float num = float.MaxValue;
					for (int i = 0; i < all.Count; i++)
					{
						UnitBase unitBase2 = all[i];
						if (!(unitBase2 == null) && !unitBase2.IsDead && !(unitBase2 == caster) && IsEnemy(caster.Faction, unitBase2.Faction))
						{
							Vector2 val = unitBase2.HitCenter - hitCenter;
							float sqrMagnitude = val.sqrMagnitude;
							float num2 = param1 + unitBase2.Radius;
							if (!(sqrMagnitude > num2 * num2) && sqrMagnitude < num)
							{
								num = sqrMagnitude;
								unitBase = unitBase2;
							}
						}
					}

					if (unitBase != null)
					{
						_scratch.Add(unitBase);
					}

					return _scratch;
				}

				for (int j = 0; j < all.Count; j++)
				{
					UnitBase unitBase3 = all[j];
					if (!(unitBase3 == null) && !unitBase3.IsDead)
					{
						Vector2 hitCenter2 = unitBase3.HitCenter;
						float radius = unitBase3.Radius;
						bool flag = false;
						switch (scanType)
						{
						case SkillScanType.Circle:
							flag = Scanner.InCircle(hitCenter, param1, hitCenter2, radius);
							break;
						case SkillScanType.Sector:
							flag = Scanner.InSector(hitCenter, facing, param1, param2, hitCenter2, radius);
							break;
						case SkillScanType.Line:
							flag = Scanner.InLine(hitCenter, facing, param1, param2, hitCenter2, radius);
							break;
						case SkillScanType.Donut:
							flag = Scanner.InDonut(hitCenter, param1, param2, hitCenter2, radius);
							break;
						}

						if (flag)
						{
							_scratch.Add(unitBase3);
						}
					}
				}

				return _scratch;
			}
			}
		}

		public static List<UnitBase> FilterByApplyTarget(List<UnitBase> scanned, SkillApplyTarget target, UnitBase caster)
		{
			if (target == SkillApplyTarget.Self)
			{
				_self.Clear();
				if (caster != null)
				{
					_self.Add(caster);
				}

				return _self;
			}

			_filtered.Clear();
			if (target == SkillApplyTarget.None || scanned == null || caster == null)
			{
				return _filtered;
			}

			if (target == SkillApplyTarget.All)
			{
				for (int i = 0; i < scanned.Count; i++)
				{
					UnitBase unitBase = scanned[i];
					if (!(unitBase == null))
					{
						_filtered.Add(unitBase);
					}
				}

				return _filtered;
			}

			for (int j = 0; j < scanned.Count; j++)
			{
				UnitBase unitBase2 = scanned[j];
				if (unitBase2 == null)
				{
					continue;
				}

				switch (target)
				{
				case SkillApplyTarget.Enemy:
					if (IsEnemy(caster.Faction, unitBase2.Faction))
					{
						_filtered.Add(unitBase2);
					}

					break;
				case SkillApplyTarget.Friendly:
					if (IsFriendly(caster.Faction, unitBase2.Faction))
					{
						_filtered.Add(unitBase2);
					}

					break;
				}
			}

			return _filtered;
		}

		private static Vector2 GetFacing(UnitBase caster)
		{
			UnitMover component = caster.GetComponent<UnitMover>();
			if (component == null)
			{
				return Vector2.right;
			}

			Vector2 facing = component.Facing;
			if (facing.sqrMagnitude < 1E-06f)
			{
				return Vector2.right;
			}

			return facing;
		}

		private static bool IsEnemy(Faction casterFaction, Faction otherFaction)
		{
			if (casterFaction == Faction.None)
			{
				return false;
			}

			if (otherFaction == Faction.None || otherFaction == Faction.Neutral)
			{
				return false;
			}

			return otherFaction != casterFaction;
		}

		private static bool IsFriendly(Faction casterFaction, Faction otherFaction)
		{
			if (casterFaction == Faction.None)
			{
				return false;
			}

			return otherFaction == casterFaction;
		}
	}
}
