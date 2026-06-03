using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 침묵 — 지속 동안 스킬 사용만 차단 (이동은 가능)
	public sealed class BUFF_SILENCE : IBuffBehavior
	{
		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		public BUFF_SILENCE(UnitBase owner, UnitBase caster)
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

			_owner.BlockSkill(nameof(BUFF_SILENCE));
		}

		public void OnDeactivate()
		{
			if (_owner == null)
			{
				return;
			}

			_owner.UnblockSkill(nameof(BUFF_SILENCE));
		}
	}
}
