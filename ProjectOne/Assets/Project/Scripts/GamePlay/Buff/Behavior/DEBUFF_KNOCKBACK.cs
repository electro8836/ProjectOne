using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 넉백 경직 — 지속 동안 이동과 스킬 사용을 모두 차단 (넉백 임펄스 자체는 입력 채널과 분리되어 그대로 적용됨)
	public sealed class DEBUFF_KNOCKBACK : IBuffBehavior
	{
		private readonly UnitBase _owner;
		private readonly UnitBase _caster;

		public DEBUFF_KNOCKBACK(UnitBase owner, UnitBase caster)
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

			_owner.BlockMove(nameof(DEBUFF_KNOCKBACK));
			_owner.BlockSkill(nameof(DEBUFF_KNOCKBACK));

			// 넉백 시 진행 중인 MotionEffectTime 대기 취소 — 경직 중 데미지 적용 방지
			if (_owner.SkillContainer != null)
			{
				_owner.SkillContainer.CancelPendingEffects();
			}
		}

		public void OnDeactivate()
		{
			if (_owner == null)
			{
				return;
			}

			_owner.UnblockMove(nameof(DEBUFF_KNOCKBACK));
			_owner.UnblockSkill(nameof(DEBUFF_KNOCKBACK));
		}
	}
}
