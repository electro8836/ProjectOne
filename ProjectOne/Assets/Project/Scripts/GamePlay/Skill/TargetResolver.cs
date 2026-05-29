using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Skill
{
	// SkillInfo.ScanType 기반 후보 산출 + SkillEffect.ApplyTarget 기반 진영 필터를 2단계로 분리
	// - ScanByType: 한 스킬 실행마다 1회 — 후보군 산출
	// - FilterByApplyTarget: 각 SkillEffect 마다 — 진영/Self 필터 적용
	// 활성 유닛은 UnitContainer 에서 가져온다. 공간 인덱스는 N이 수백을 크게 넘는 경우 재검토.
	public static class TargetResolver
	{
		// 스캔 결과 — Executor 가 effect 들에 전파. 다음 ScanByType 호출 시까지 유효.
		static readonly List<UnitBase> _scratch = new List<UnitBase>(32);
		// ApplyTarget 필터 결과 — effect 마다 갱신. _scratch 와 별개 리스트여야 함.
		static readonly List<UnitBase> _filtered = new List<UnitBase>(32);
		// Self 결과 — caster 1명. effect 마다 재사용.
		static readonly List<UnitBase> _self = new List<UnitBase>(1);

		// ScanType 별 후보 산출. 결과는 호출자가 즉시 소비 (다음 호출 시 덮어씀).
		public static List<UnitBase> ScanByType(SkillScanType scanType, int param1, int param2, UnitBase caster)
		{
			_scratch.Clear();
			if (caster == null)
			{
				return _scratch;
			}
			if (scanType == SkillScanType.None)
			{
				return _scratch;
			}
			if (scanType == SkillScanType.Chain)
			{
				Debug.Log("[TargetResolver] Chain TODO — 빈 결과 반환");
				return _scratch;
			}

			Vector2 origin = caster.transform.position;
			Vector2 facing = GetFacing(caster);
			float p1 = param1;
			float p2 = param2;

			IReadOnlyList<UnitBase> all = UnitContainer.Instance.All;

			if (scanType == SkillScanType.Target)
			{
				// 거리 p1 이내 적 중 최단 1명만
				UnitBase nearest = null;
				float nearestSqr = float.MaxValue;
				float p1Sqr = p1 * p1;
				for (int i = 0; i < all.Count; i++)
				{
					UnitBase u = all[i];
					if (u == null || u.IsDead == true || u == caster)
					{
						continue;
					}
					if (IsEnemy(caster.Faction, u.Faction) == false)
					{
						continue;
					}
					Vector2 p = u.transform.position;
					float ds = (p - origin).sqrMagnitude;
					if (ds > p1Sqr)
					{
						continue;
					}
					if (ds < nearestSqr)
					{
						nearestSqr = ds;
						nearest = u;
					}
				}
				if (nearest != null)
				{
					_scratch.Add(nearest);
				}
				return _scratch;
			}

			// 면적 스캔 (Circle/Sector/Line/Donut)
			for (int i = 0; i < all.Count; i++)
			{
				UnitBase u = all[i];
				if (u == null || u.IsDead == true)
				{
					continue;
				}
				Vector2 point = u.transform.position;
				bool hit = false;
				if (scanType == SkillScanType.Circle)
				{
					hit = Scanner.InCircle(origin, p1, point);
				}
				else if (scanType == SkillScanType.Sector)
				{
					hit = Scanner.InSector(origin, facing, p1, p2, point);
				}
				else if (scanType == SkillScanType.Line)
				{
					hit = Scanner.InLine(origin, facing, p1, p2, point);
				}
				else if (scanType == SkillScanType.Donut)
				{
					hit = Scanner.InDonut(origin, p1, p2, point);
				}

				if (hit == true)
				{
					_scratch.Add(u);
				}
			}
			return _scratch;
		}

		// ApplyTarget 기반 진영/Self 필터. 결과는 호출자가 즉시 소비.
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
					UnitBase u = scanned[i];
					if (u == null)
					{
						continue;
					}
					_filtered.Add(u);
				}
				return _filtered;
			}

			for (int i = 0; i < scanned.Count; i++)
			{
				UnitBase u = scanned[i];
				if (u == null)
				{
					continue;
				}
				if (target == SkillApplyTarget.Enemy)
				{
					if (IsEnemy(caster.Faction, u.Faction) == true)
					{
						_filtered.Add(u);
					}
				}
				else if (target == SkillApplyTarget.Friendly)
				{
					if (IsFriendly(caster.Faction, u.Faction) == true)
					{
						_filtered.Add(u);
					}
				}
			}
			return _filtered;
		}

		static Vector2 GetFacing(UnitBase caster)
		{
			UnitMover mover = caster.GetComponent<UnitMover>();
			if (mover == null)
			{
				return Vector2.right;
			}
			Vector2 f = mover.Facing;
			if (f.sqrMagnitude < 1e-6f)
			{
				return Vector2.right;
			}
			return f;
		}

		static bool IsEnemy(Faction casterFaction, Faction otherFaction)
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

		static bool IsFriendly(Faction casterFaction, Faction otherFaction)
		{
			if (casterFaction == Faction.None)
			{
				return false;
			}
			return otherFaction == casterFaction;
		}
	}
}
