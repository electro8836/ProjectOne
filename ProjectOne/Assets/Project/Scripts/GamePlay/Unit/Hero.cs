using EDT;
using UnityEngine;
using ProjectOne.Combat;
using ProjectOne.Mastery;
using ProjectOne.Skill;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	[RequireComponent(typeof(UnitMover), typeof(UnitAnimator))]
	public class Hero : UnitBase, IDamageable
	{
		// 스킬 리졸브 캐시 — 히어로만 모디파이어 출처(스킬 트리·장비)를 가진다 (스킬 설계 11.1).
		private readonly SkillResolver _skillResolver = new SkillResolver();

		public SkillResolver SkillResolver
		{
			get { return _skillResolver; }
		}

		public override UnitType GetUnitType()
		{
			return UnitType.Hero;
		}

		// 장착 무기의 마스터리가 근접/원거리를 결정한다. 무기를 바꾸면 즉시 뒤집힌다 (설계 10.4).
		public override WeaponRangeType RangeType
		{
			get
			{
				Table_WeaponMastery.Row mastery = Account.Instance.Mastery.CurrentMastery;
				return mastery != null ? mastery.RangeType : WeaponRangeType.None;
			}
		}

		public override ResolvedSkill Resolve(EDT.Skill id)
		{
			return _skillResolver.Resolve(id);
		}

		public void TakeDamage(in DamageInfo info)
		{
			HandleHit(info);

			if (_vitals != null)
			{
				_vitals.ModifyHp(-info.Damage);
				if (_vitals.IsHpZero)
				{
					Die();
				}
			}
		}
	}
}
