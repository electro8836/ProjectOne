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

		// Target 스캔용 후보 거리(sqrMagnitude) — _scratch 와 인덱스 1:1 대응
		private static readonly List<float> _scratchDist = new List<float>(32);

		// scanRange 의 의미는 형태마다 다르다 — Circle/Sector=반경, Line=길이, Target=사거리 (설계 3.4).
		// scanParam 은 Sector=각도(도), Line=폭, Target=최대 대상 수. Circle 은 미사용.
		public static List<UnitBase> ScanByType(SkillScanTypes scanType, float scanRange, float scanParam, UnitBase caster, bool useOverride = false, Vector2 centerOverride = default, Vector2 facingOverride = default)
		{
			_scratch.Clear();
			if (caster == null)
			{
				return _scratch;
			}

			switch (scanType)
			{
			case SkillScanTypes.None:
				return _scratch;
			default:
			{
				Vector2 hitCenter = useOverride ? centerOverride : caster.HitCenter;
				Vector2 facing = useOverride ? facingOverride : GetFacing(caster);
				IReadOnlyList<UnitBase> all = UnitManager.Instance.All;
				if (scanType == SkillScanTypes.Target)
				{
					// 대상 수 제한은 Target 전용이다. 범위 공격은 범위 안을 전부 타격한다 (설계 2.4).
					int count = (scanParam < 1f) ? 1 : (int)scanParam;
					// 반경 내 유효한 적을 모두 후보로 수집 (거리 함께 보관)
					_scratchDist.Clear();
					for (int i = 0; i < all.Count; i++)
					{
						UnitBase unitBase2 = all[i];
						if (!(unitBase2 == null) && !unitBase2.IsDead && !(unitBase2 == caster) && IsEnemy(caster.Faction, unitBase2.Faction))
						{
							Vector2 val = unitBase2.HitCenter - hitCenter;
							float sqrMagnitude = val.sqrMagnitude;
							float num2 = scanRange + unitBase2.Radius;
							if (!(sqrMagnitude > num2 * num2))
							{
								_scratch.Add(unitBase2);
								_scratchDist.Add(sqrMagnitude);
							}
						}
					}

					// 가장 가까운 count 명을 앞쪽으로 부분 선택정렬 (후보가 적으면 있는 만큼만)
					int select = (count < _scratch.Count) ? count : _scratch.Count;
					for (int k = 0; k < select; k++)
					{
						int minIdx = k;
						for (int m = k + 1; m < _scratch.Count; m++)
						{
							if (_scratchDist[m] < _scratchDist[minIdx])
							{
								minIdx = m;
							}
						}

						if (minIdx != k)
						{
							UnitBase tmpUnit = _scratch[k];
							_scratch[k] = _scratch[minIdx];
							_scratch[minIdx] = tmpUnit;
							float tmpDist = _scratchDist[k];
							_scratchDist[k] = _scratchDist[minIdx];
							_scratchDist[minIdx] = tmpDist;
						}
					}

					// count 초과분 제거
					if (_scratch.Count > select)
					{
						_scratch.RemoveRange(select, _scratch.Count - select);
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
						case SkillScanTypes.Circle:
							flag = Scanner.InCircle(hitCenter, scanRange, hitCenter2, radius);
							break;
						case SkillScanTypes.Sector:
							flag = Scanner.InSector(hitCenter, facing, scanRange, scanParam, hitCenter2, radius);
							break;
						case SkillScanTypes.Line:
							flag = Scanner.InLine(hitCenter, facing, scanRange, scanParam, hitCenter2, radius);
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

		// AI 타겟팅 등 외부에서 적군 판정 재사용 — None/Neutral 은 적 아님
		public static bool IsEnemy(Faction casterFaction, Faction otherFaction)
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
