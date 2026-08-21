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

		// Target 스캔용 후보 거리(sqrMagnitude) — _scratch 와 인덱스 1:1 대응
		private static readonly List<float> _scratchDist = new List<float>(32);

		// scanRange 의 의미는 형태마다 다르다 — Circle/Sector=반경, Line=길이, Target=사거리 (설계 3.4).
		// scanParam 은 Sector=각도(도), Line=폭, Target=최대 대상 수. Circle 은 미사용.
		//
		// 진영 판정(applyTarget)은 기하 판정보다 싸므로 루프 안에서 먼저 건다.
		// 별도 필터 패스를 두면 버퍼를 한 번 더 복사하게 되고, 거르지 않은 목록이
		// 외부로 새어나가 아군 오폭이 된다(이 함수가 유일한 탐색 진입점이다).
		public static List<UnitBase> ScanByType(SkillScanTypes scanType, SkillApplyTarget applyTarget, float scanRange, float scanParam, UnitBase caster, bool useOverride = false, Vector2 centerOverride = default, Vector2 facingOverride = default)
		{
			_scratch.Clear();
			if (caster == null || applyTarget == SkillApplyTarget.None)
			{
				return _scratch;
			}

			// 자기 대상은 탐색하지 않는다 — ScanRange 값과 무관하다 (설계 3.5).
			if (applyTarget == SkillApplyTarget.Self)
			{
				_scratch.Add(caster);
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
						// 지정 대상은 자기 자신을 고르지 않는다 — 진영 판정과 별개로 유지한다.
						if (!(unitBase2 == null) && !unitBase2.IsDead && !(unitBase2 == caster) && passesApplyTarget(applyTarget, caster, unitBase2))
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
					// 진영 판정을 기하 판정 앞에 둔다 — 후보를 먼저 줄여야 InSector/InLine 호출이 줄어든다.
					if (!(unitBase3 == null) && !unitBase3.IsDead && passesApplyTarget(applyTarget, caster, unitBase3))
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

		// 유닛 하나가 ApplyTarget 진영 조건을 통과하는가.
		//
		// Enemy 는 같은 진영을 걸러내므로 시전자가 자동으로 빠진다.
		// Friendly 는 시전자를 포함한다 — 자힐·자버프가 동작해야 한다.
		private static bool passesApplyTarget(SkillApplyTarget target, UnitBase caster, UnitBase unit)
		{
			switch (target)
			{
			case SkillApplyTarget.Enemy:
				return IsEnemy(caster.Faction, unit.Faction);
			case SkillApplyTarget.Friendly:
				return IsFriendly(caster.Faction, unit.Faction);
			case SkillApplyTarget.All:
				return true;
			default:
				return false;
			}
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
