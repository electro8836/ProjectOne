using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 이동불가(속박) — 지속 동안 이동만 차단 (스킬은 가능)
	public sealed class DEBUFF_BINDING : IBuffBehavior
	{
		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		public DEBUFF_BINDING(UnitBase owner, UnitBase caster)
		{
			_owner = owner;
			_caster = caster;
		}

		public void OnActivate()
		{
			if (_owner == null)
			{
				return;
			}

			_owner.BlockMove(nameof(DEBUFF_BINDING));
		}

		public void OnDeactivate()
		{
			if (_owner == null)
			{
				return;
			}

			_owner.UnblockMove(nameof(DEBUFF_BINDING));
		}
	}
}
