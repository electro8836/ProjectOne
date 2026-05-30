using UnityEngine;
using ProjectOne.Combat;
using ProjectOne.Utils;

namespace ProjectOne.Unit
{
	[RequireComponent(typeof(UnitMover), typeof(UnitAnimator))]
	public class Monster : UnitBase, IDamageable, IPoolable
	{
		public override UnitType GetUnitType()
		{
			return UnitType.Monster;
		}

		public void TakeDamage(in DamageInfo info)
		{
			HandleHit(in info);
			if (_vitals != null)
			{
				_vitals.ModifyHp(-info.Damage);
				if (_vitals.IsHpZero)
				{
					Die();
				}
			}
		}

		public void OnActivate()
		{
		}

		public void OnDeactivate()
		{
		}

		void IDamageable.TakeDamage(in DamageInfo info)
		{
			TakeDamage(in info);
		}
	}
}
