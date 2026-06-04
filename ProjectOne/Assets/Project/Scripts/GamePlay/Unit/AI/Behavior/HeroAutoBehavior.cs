namespace ProjectOne.Unit.AI
{
	// 히어로 자동전투 전략 — 이동/시선은 플레이어(HeroController + UnitMover)가 담당하고,
	// AI 는 스킬의 실제 범위 안에 적이 있으면 사용 가능한 스킬을 자동 시전만 한다.
	// 고유스킬(Special)은 HUD 버튼 수동이므로 castSpecial=false.
	public sealed class HeroAutoBehavior : IAiBehavior
	{
		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			SkillSelector.Select(self, false);
		}
	}
}
