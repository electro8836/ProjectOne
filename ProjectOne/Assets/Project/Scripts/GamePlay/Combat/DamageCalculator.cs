using EDT;
using UnityEngine;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;

namespace ProjectOne.Combat
{
	// 데미지 계산식 (설계 10.1). 순서를 바꾸면 결과가 달라지므로 단계 번호를 주석으로 고정한다.
	//
	// [0] 회피  [1] 막기  [2] 기본피해  [3] 치명타  [4] 피해 보너스
	// [5] 방어 감쇄  [6] 피해 감소  [7] 받는 피해  [8] 최종 증폭  [9] 흡혈
	public static class DamageCalculator
	{
		// 계산 결과 — 흡혈은 호출자가 시전자에게 적용해야 하므로 함께 돌려준다.
		public struct Result
		{
			public int Damage;
			public bool IsCritical;
			public bool IsAvoided;		// 회피 또는 막기로 무효화
			public bool IsBlocked;		// IsAvoided 중 막기 쪽 — 회피(MISS)와 표시를 구분하려고 둔다
			public float LifeOnHit;
		}

		// scaleStat: 비례 기준 스탯 (Damage 효과의 ScaleStat). 기준 주체는 항상 시전자다.
		// isNormalAttack: 평타 피해인가 — 평타 피해량/스킬 피해량 중 어느 쪽을 더할지 정한다.
		public static Result Calculate(UnitBase attacker, UnitBase target, Stat scaleStat, float ratio, float flatValue, bool triggersOnHit, bool isNormalAttack)
		{
			Result r = default(Result);
			if (attacker == null || target == null || attacker.Stats == null || target.Stats == null)
			{
				return r;
			}

			StatContainer a = attacker.Stats;
			StatContainer t = target.Stats;

			// [0] 회피 판정
			if (Random.value < t.GetStat(Stat.Stat_EvasionRate))
			{
				r.IsAvoided = true;
				return r;
			}

			// [1] 막기 판정
			if (Random.value < t.GetStat(Stat.Stat_BlockRate))
			{
				r.IsAvoided = true;
				r.IsBlocked = true;
				return r;
			}

			// [2] 기본 피해 — ScaleStat 의 기준 주체는 항상 시전자
			float scaleValue = (scaleStat != Stat.None) ? a.GetStat(scaleStat) : 0f;
			float damage = scaleValue * ratio + flatValue;

			// [3] 치명타
			if (Random.value < a.GetStat(Stat.Stat_CritRate))
			{
				r.IsCritical = true;
				damage *= a.GetStat(Stat.Stat_CritDamage);
			}

			// [3-1] 2배 피해 — 치명타와 독립된 판정이다. 스킬 피해는 스킬 전용 확률을 더해 한 번만 굴린다.
			float doubleRate = a.GetStat(Stat.Stat_DoubleDamageRate);
			if (isNormalAttack == false)
			{
				doubleRate += a.GetStat(Stat.Stat_SkillDoubleDamageRate);
			}

			if (Random.value < doubleRate)
			{
				damage *= 2f;
			}

			// [4] 피해 보너스 — 합연산 레이어.
			//
			// 근접/원거리는 장착 무기의 WeaponMastery.RangeType 이 결정하며 스킬별 구분은 없다 (설계 10.4).
			// 원거리 무기로 근접 모션 스킬을 써도 원거리 보너스를 받는다.
			// 몬스터는 두 스탯을 쓰지 않으므로 RangeType 이 None 이고 이 항이 0이다.
			float bonus = a.GetStat(Stat.Stat_DamageBonus);
			switch (attacker.RangeType)
			{
				case WeaponRangeType.Melee:
					bonus += a.GetStat(Stat.Stat_MeleeDamageBonus);
					break;
				case WeaponRangeType.Ranged:
					bonus += a.GetStat(Stat.Stat_RangeDamageBonus);
					break;
			}

			// 평타/스킬 보너스 — 평타는 평타 스킬과 그 평타를 다시 치는 효과, 그 외는 전부 스킬 피해다.
			// 몬스터는 두 스탯을 쓰지 않으므로 이 항이 0이다.
			if (isNormalAttack == true)
			{
				bonus += a.GetStat(Stat.Stat_NormalAttackDamageBonus);

				// 출혈 중첩 비례 — 대상의 출혈 1중첩마다 평타 피해량을 더한다 (단검 상처 벌리기).
				float perBleed = a.GetStat(Stat.Stat_NormalDamagePerBleedStack);
				if (perBleed > 0f && target.BuffContainer != null)
				{
					bonus += perBleed * target.BuffContainer.GetStack(SkillConstants.BLEED_BUFF);
				}
			}
			else
			{
				bonus += a.GetStat(Stat.Stat_SkillDamageBonus);

				// 공격 속도 초과분 전환 — 100%를 넘는 공속에 전환율을 곱해 스킬 피해량에 더한다 (수리검 질풍).
				float toSkill = a.GetStat(Stat.Stat_AtkSpeedToSkillDamage);
				if (toSkill > 0f)
				{
					float overSpeed = a.GetStat(Stat.Stat_AtkSpeed) - 1f;
					if (overSpeed > 0f)
					{
						bonus += overSpeed * toSkill;
					}
				}
			}

			// 보스 보너스 — 대상이 보스 등급일 때만 (몬스터 설계 2장).
			if (target.MonsterType == MonsterType.Boss)
			{
				bonus += a.GetStat(Stat.Stat_BossDamageBonus);
			}

			// 저체력 대상 보너스 — 대상 체력 비율이 LOW_HP_TARGET_RATIO 이하일 때만 (단검 처형자).
			float lowHpBonus = a.GetStat(Stat.Stat_LowHpTargetDamageBonus);
			if (lowHpBonus > 0f && GetHpRatio(target) <= SkillConstants.LOW_HP_TARGET_RATIO)
			{
				bonus += lowHpBonus;
			}

			damage *= 1f + bonus;

			// [5] 방어 감쇄 — 제산식. K 의 기준은 전투에 낀 몬스터의 레벨이다 (스킬 설계 10.3).
			// 몬스터가 때리면 그 몬스터, 히어로·소환물이 때리면 맞는 몬스터 — 플레이어 쪽에는 레벨이 없다.
			float effectiveDef = t.GetStat(Stat.Stat_Def) * (1f - a.GetStat(Stat.Stat_DefPen));
			if (effectiveDef < 0f)
			{
				effectiveDef = 0f;
			}

			int monsterLevel = (attacker.GetUnitType() == UnitType.Monster) ? attacker.Level : target.Level;
			float k = 50f + monsterLevel * 20f;
			float reduction = effectiveDef / (effectiveDef + k);
			damage *= 1f - reduction;

			// [6] 피해 감소
			damage *= 1f - t.GetStat(Stat.Stat_DamageReduction);

			// [7] 받는 피해량
			damage *= 1f + t.GetStat(Stat.Stat_DamageTakenAmp);

			// [8] 최종 증폭 — 보너스와 독립된 곱연산 레이어
			damage *= 1f + a.GetStat(Stat.Stat_FinalDamageAmp);

			if (damage < 0f)
			{
				damage = 0f;
			}

			r.Damage = Mathf.RoundToInt(damage);

			// [9] 흡혈 — OnHitTrigger 가 TRUE 인 효과만
			if (triggersOnHit == true)
			{
				r.LifeOnHit = a.GetStat(Stat.Stat_LifeOnHit);
			}

			return r;
		}

		// 현재 체력 비율(0~1). 체력 정보가 없으면 1(만피)로 본다 — 처형 조건이 잘못 켜지지 않게.
		public static float GetHpRatio(UnitBase unit)
		{
			if (unit == null || unit.Vitals == null || unit.Stats == null)
			{
				return 1f;
			}

			float maxHp = unit.Stats.GetStat(Stat.Stat_MaxHp);
			if (maxHp <= 0f)
			{
				return 1f;
			}

			return unit.Vitals.Hp / maxHp;
		}

		// 회복량 (설계 10.7) — 치명타·방어·감소를 적용하지 않는다.
		public static int CalculateHeal(UnitBase caster, Stat scaleStat, float ratio, float flatValue)
		{
			if (caster == null || caster.Stats == null)
			{
				return 0;
			}

			float scaleValue = (scaleStat != Stat.None) ? caster.Stats.GetStat(scaleStat) : 0f;
			float amount = scaleValue * ratio + flatValue;
			if (amount < 0f)
			{
				amount = 0f;
			}

			return Mathf.RoundToInt(amount);
		}
	}
}
