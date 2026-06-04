namespace ProjectOne.Unit.AI
{
	// 유닛 자동전투 두뇌 (POCO) — UnitBase.LateUpdate 에서 Tick 위임 호출.
	// 전략(IAiBehavior)에 모든 판단을 위임한다. 히어로 behavior 는 이동/시선을 건드리지 않고 스킬만 시전.
	public sealed class AiBrain
	{
		private readonly UnitBase _owner;
		private readonly IAiBehavior _behavior;
		private readonly Blackboard _bb;

		public AiBrain(UnitBase owner, IAiBehavior behavior)
		{
			_owner = owner;
			_behavior = behavior;
			_bb = new Blackboard();
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
