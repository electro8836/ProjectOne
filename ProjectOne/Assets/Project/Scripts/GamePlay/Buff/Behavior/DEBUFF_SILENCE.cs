using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 침묵 — 지속 동안 스킬 사용만 차단 (이동은 가능)
	public sealed class DEBUFF_SILENCE : IBuffBehavior
	{
		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		public DEBUFF_SILENCE(UnitBase owner, UnitBase caster)
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

			_owner.BlockSkill(nameof(DEBUFF_SILENCE));

			// 침묵 시 진행 중인 캐스팅 취소
			_owner.SkillContainer?.CancelCasting();
		}

		public void OnDeactivate()
		{
			if (_owner == null)
			{
				return;
			}

			_owner.UnblockSkill(nameof(DEBUFF_SILENCE));
		}
	}
}
