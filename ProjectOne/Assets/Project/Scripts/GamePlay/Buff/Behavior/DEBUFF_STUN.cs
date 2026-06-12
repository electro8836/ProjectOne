using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 스턴 — 지속 동안 이동과 스킬 사용을 모두 차단
	public sealed class DEBUFF_STUN : IBuffBehavior
	{
		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		public DEBUFF_STUN(UnitBase owner, UnitBase caster)
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

			_owner.BlockMove(nameof(DEBUFF_STUN));
			_owner.BlockSkill(nameof(DEBUFF_STUN));

			// 스턴 시 진행 중인 캐스팅 / 코드 스킬(대시 등) 취소
			_owner.SkillContainer?.CancelCasting();
			_owner.SkillContainer?.CancelBehavior();
		}

		public void OnDeactivate()
		{
			if (_owner == null)
			{
				return;
			}

			_owner.UnblockMove(nameof(DEBUFF_STUN));
			_owner.UnblockSkill(nameof(DEBUFF_STUN));
		}
	}
}
