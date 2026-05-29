using UnityEngine;
using ProjectOne.Combat;

namespace ProjectOne.Unit
{
	[RequireComponent(typeof(UnitMover), typeof(UnitAnimator))]
	public class Monster : UnitBase, IDamageable
	{
		public override UnitType GetUnitType()
		{
			return UnitType.Monster;
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
