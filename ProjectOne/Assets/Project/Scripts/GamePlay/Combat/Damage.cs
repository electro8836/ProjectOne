using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Combat
{
	public enum DamageType 
	{
		None,
		Physical,
		Magical,
		True
	}

	public interface IDamageable
	{
		void TakeDamage(in DamageInfo info);
		bool IsDead { get; }
	}
	
	public struct DamageInfo
	{
		public UnitBase Attacker;
		public int Damage;
		public DamageType DamageType;
		public Vector2 HitPoint;
		public Vector2 KnockbackDir;
		public float KnockbackPower;
		public bool IsCritical;
		public bool IsSuperCritical;
		public int SkillID;
	}
}