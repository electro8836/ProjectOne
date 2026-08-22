using EDT;
using UnityEngine;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;

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
		public static Result Calculate(UnitBase attacker, UnitBase target, Stat scaleStat, float ratio, float flatValue, bool triggersOnHit)
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

			// 보스 보너스 — 대상이 보스 등급일 때만 (몬스터 설계 2장).
			if (target.MonsterType == MonsterType.Boss)
			{
				bonus += a.GetStat(Stat.Stat_BossDamageBonus);
			}

			damage *= 1f + bonus;

			// [5] 방어 감쇄 — 제산식. K 의 기준은 공격자 레벨이다.
			float effectiveDef = t.GetStat(Stat.Stat_Def) * (1f - a.GetStat(Stat.Stat_DefPen));
			if (effectiveDef < 0f)
			{
				effectiveDef = 0f;
			}

			float k = 50f + attacker.Level * 20f;
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
