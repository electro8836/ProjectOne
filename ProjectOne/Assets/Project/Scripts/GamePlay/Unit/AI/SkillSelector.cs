using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Skill;
using ProjectOne.Map;

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

			IReadOnlyList<EDT.Skill> all = sc.GetAll();
			for (int i = 0; i < all.Count; i++)
			{
				EDT.Skill id = all[i];

				// 사거리·탐색 형태는 모디파이어 대상이므로 리졸브 결과로 판단해야 한다.
				ResolvedSkill resolved = self.Resolve(id);
				if (resolved == null || resolved.IsValid == false)
				{
					continue;
				}

				Table_Skill.Row row = resolved.Row;

				// 평타는 폴백용으로 보류, 조건 발동형·상시형은 직접 시전 대상 아님
				if (row.SkillCategory == SkillCategoryTypes.Normal)
				{
					continue;
				}

				if (SkillContainer.IsDirectCastable(row.CastingType) == false)
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
			EDT.Skill basic = sc.GetBasicAttack();
			if (basic == EDT.Skill.None)
			{
				return;
			}

			ResolvedSkill basicResolved = self.Resolve(basic);
			if (basicResolved == null || basicResolved.IsValid == false || sc.IsOnCooldown(basic) == true)
			{
				return;
			}

			if (HasEnemyInRange(self, basicResolved.Row) == true)
			{
				sc.TryCast(basic);
			}
		}

		// 스킬의 실제 ScanType 범위(caster.Facing 기준) 안에 시전 가능한 적이 1명 이상 있는지 — SkillExecutor 와 동일 경로.
		// 발사체 스킬이면 경로상 차단 벽이 없는(시야 확보된) 적이 1명이라도 있어야 한다(벽에 대고 헛스킬 방지).
		private static bool HasEnemyInRange(UnitBase self, Table_Skill.Row row)
		{
			// 적 존재 여부만 보므로 row.ApplyTarget 이 아니라 Enemy 고정이다 — 지원 스킬이 생기면 재검토한다.
			List<UnitBase> enemies = TargetResolver.ScanByType(row.ScanType, SkillApplyTarget.Enemy, row.ScanRange, row.ScanParam, self);
			if (enemies.Count == 0)
			{
				return false;
			}

			// 발사체 스킬이 아니거나 맵이 없으면 범위 내 적 존재만으로 시전 가능
			if (IsProjectileSkill(row) == false || MapManager.HasInstance == false)
			{
				return true;
			}

			// enemies 는 TargetResolver 내부 버퍼 직참조 — 이 루프 중 다른 ScanByType 호출 없음(HasLineOfSight 는 맵 질의)
			Vector2 from = self.HitCenter;
			for (int i = 0; i < enemies.Count; i++)
			{
				if (MapManager.Instance.HasLineOfSight(from, enemies[i].HitCenter) == true)
				{
					return true;
				}
			}

			return false;
		}

		// 효과 슬롯 중 하나라도 Projectile 이면 발사체 스킬 (MonsterApproachBehavior 의 정지/접근 판정에서도 사용)
		internal static bool IsProjectileSkill(Table_Skill.Row row)
		{
			return IsProjectileEffect(row.EffectID_01) || IsProjectileEffect(row.EffectID_02);
		}

		private static bool IsProjectileEffect(SkillEffect id)
		{
			if (id == SkillEffect.None)
			{
				return false;
			}

			Table_SkillEffect.Row row = Table_SkillEffect.Get(id);
			return row != null && row.EffectType == SkillEffectTypes.Projectile;
		}
	}
}
