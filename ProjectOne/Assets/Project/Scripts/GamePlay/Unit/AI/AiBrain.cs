namespace ProjectOne.Unit.AI
{
	// 유닛 자동전투 두뇌 (POCO) — UnitBase.LateUpdate 에서 Tick 위임 호출.
	// 전략(IAiBehavior)에 모든 판단을 위임한다. 히어로 behavior 는 이동/시선을 건드리지 않고 스킬만 시전.
	public sealed class AiBrain
	{
		private readonly UnitBase _owner;
		private readonly IAiBehavior _behavior;
		private readonly Blackboard _bb;

		// 스폰마다 앵커·타겟을 초기화해야 하므로 외부에서 접근한다(풀 재사용 대비).
		public Blackboard Blackboard
		{
			get { return _bb; }
		}

		public AiBrain(UnitBase owner, IAiBehavior behavior)
		{
			_owner = owner;
			_behavior = behavior;
			_bb = new Blackboard();
		}

		// 피격 알림 — 비선공(Neutral) 몬스터를 각성시킨다 (몬스터 설계 2장).
		public void OnDamaged(UnitBase attacker)
		{
			if (_bb.Provoked == true || attacker == null)
			{
				return;
			}

			_bb.Provoked = true;
			_bb.Target = attacker;
		}

		public void Tick(float dt)
		{
			if (_owner.IsDead == true)
			{
				return;
			}

			_behavior.Tick(_owner, _bb, dt);
		}
	}
}
