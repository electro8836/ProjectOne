namespace ProjectOne.Unit.AI
{
	// 유닛 유형별(히어로/몹/보스) AI 전략 — AiBrain 이 매 틱 위임 호출.
	// 이동 유무는 behavior 가 자체 결정한다. 히어로는 이동/시선을 건드리지 않고 스킬만 시전.
	public interface IAiBehavior
	{
		void Tick(UnitBase self, Blackboard bb, float dt);
	}
}
