using System.Collections.Generic;
using EDT;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 우선순위 스킬 셀렉터 — 보유 순서대로, 스킬의 실제 ScanType 범위(facing 기준) 안에 적이 있고
	// 쿨다운이 아니면 시전. 첫 성공에서 멈추고, 모두 실패하면 기본공격으로 폴백.
	// 쿨/차단/생존 가드는 SkillContainer.TryCast 에 위임.
	public static class SkillSelector
	{
		public static void Select(UnitBase self, bool castSpecial)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return;
			}

			IReadOnlyList<SkillInfo> all = sc.GetAll();
			for (int i = 0; i < all.Count; i++)
			{
				SkillInfo id = all[i];
				Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
				if (row == null)
				{
					continue;
				}

				// 기본공격은 폴백용으로 보류, 패시브/온힛은 직접 시전 대상 아님
				if (row.IsBasicAttack == true)
				{
					continue;
				}

				if (row.CastingType == SkillCastingTypes.Passive || row.CastingType == SkillCastingTypes.OnHit)
				{
					continue;
				}

				// 고유(Special) 스킬은 HUD 버튼 수동 (castSpecial=true 인 PVP 등만 자동)
				if (sc.IsSpecial(id) == true && castSpecial == false)
				{
					continue;
				}

				if (sc.IsOnCooldown(id) == true)
				{
					continue;
				}

				if (HasEnemyInRange(self, row) == false)
				{
					continue;
				}

				if (sc.TryCast(id) == true)
				{
					return;
				}
			}

			// 폴백 — 기본 공격 (범위 내 적이 있을 때만)
			SkillInfo basic = sc.GetBasicAttack();
			if (basic == SkillInfo.None)
			{
				return;
			}

			Table_SkillInfo.Row basicRow = Table_SkillInfo.Get(basic);
			if (basicRow == null || sc.IsOnCooldown(basic) == true)
			{
				return;
			}

			if (HasEnemyInRange(self, basicRow) == true)
			{
				sc.TryCast(basic);
			}
		}

		// 스킬의 실제 ScanType 범위(caster.Facing 기준) 안에 적이 1명 이상 있는지 — SkillExecutor 와 동일 경로
		private static bool HasEnemyInRange(UnitBase self, Table_SkillInfo.Row row)
		{
			List<UnitBase> scanned = TargetResolver.ScanByType(row.ScanType, row.ScanParam1, row.ScanParam2, self);
			List<UnitBase> enemies = TargetResolver.FilterByApplyTarget(scanned, SkillApplyTarget.Enemy, self);
			return enemies.Count > 0;
		}
	}
}
