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

		// 무기 조준·배치 — 무기를 손에서 분리한 프리팹만 붙는다. 없으면 null.
		private HeroWeapon _weapon;

		public HeroWeapon Weapon
		{
			get { return _weapon; }
		}

		protected override void Awake()
		{
			base.Awake();
			_weapon = this.GetComponent<HeroWeapon>();
		}

		// base 가 UnitAnimator 좌우 반전까지 끝낸 뒤에 무기를 돌린다 — 반전 결과로 조준각을 구한다.
		public override void ManualTick(float dt)
		{
			// 공격 모션이 도는 동안 몸 반전을 보류한다(지난 프레임 무기 상태 기준)
			if (_weapon != null && _animator != null)
			{
				_animator.SetFlipHeld(_weapon.HoldsFlip);
			}

			base.ManualTick(dt);

			if (_weapon != null && _mover != null && IsDead == false)
			{
				// 조준 잠금 중이거나, 잠그지 않는 행동(대상 지정 평타 — 소총·수리검) 중이면 무기가 facing 을 따른다
				bool aimFollows = _mover.IsFacingLocked == true || (SkillContainer != null && SkillContainer.IsInAction == true);
				_weapon.Tick(_mover.Facing, aimFollows, dt);
			}

			// 정렬은 사망 후에도 몸과 함께 따라가야 하므로 생존과 무관하게 맞춘다
			if (_weapon != null)
			{
				_weapon.SyncSorting();
			}
		}

		// 사망 모션은 쓰러지는데 무기는 공중에 남으므로 숨긴다
		protected override void Die()
		{
			base.Die();

			if (_weapon != null)
			{
				_weapon.SetVisible(false);
			}
		}

		public override void OnSpawnReset(Vector3 pos)
		{
			base.OnSpawnReset(pos);

			if (_weapon != null)
			{
				_weapon.SetVisible(true);
			}
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
