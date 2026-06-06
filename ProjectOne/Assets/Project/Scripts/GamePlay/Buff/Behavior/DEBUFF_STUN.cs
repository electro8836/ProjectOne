using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 스턴 — 지속 동안 이동과 스킬 사용을 모두 차단
	public sealed class BUFF_STUN : IBuffBehavior
	{
		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		public BUFF_STUN(UnitBase owner, UnitBase caster)
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

			_owner.BlockMove(nameof(BUFF_STUN));
			_owner.BlockSkill(nameof(BUFF_STUN));

			// 스턴 시 진행 중인 캐스팅 취소
			_owner.SkillContainer?.CancelCasting();
		}

		public void OnDeactivate()
		{
			if (_owner == null)
			{
				return;
			}

			_owner.UnblockMove(nameof(BUFF_STUN));
			_owner.UnblockSkill(nameof(BUFF_STUN));
		}
	}
}
