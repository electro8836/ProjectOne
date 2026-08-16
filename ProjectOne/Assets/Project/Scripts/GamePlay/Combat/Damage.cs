using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Combat
{
	public interface IDamageable
	{
		void TakeDamage(in DamageInfo info);
		bool IsDead { get; }
	}

	// 한 번의 타격 결과. 물리/마법 구분은 두지 않는다 —
	// 신규 스탯에 마법방어·마법관통이 없고 설계 10.1 계산식에도 구분이 없다.
	public struct DamageInfo
	{
		public UnitBase Attacker;
		public int Damage;
		public Vector2 HitPoint;
		public Vector2 KnockbackDir;
		public float KnockbackPower;
		public bool IsCritical;
		public int SkillID;

		// 이 타격이 온히트 계열(OnHit/OnCrit/Stat_LifeOnHit)을 발동시키는가.
		// SkillEffect.OnHitTrigger 를 그대로 실어 나른다.
		public bool TriggersOnHit;
	}
}
