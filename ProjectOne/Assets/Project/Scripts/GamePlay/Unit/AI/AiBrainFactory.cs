namespace ProjectOne.Unit.AI
{
	// AI 타입 분기를 한 곳에 격리 — 유닛 정보로 적절한 behavior 를 만들어 AiBrain 을 생성.
	public static class AiBrainFactory
	{
		// 히어로: 이동/시선은 플레이어가 하고 AI 는 스킬 자동 시전만 (근접/원거리 구분 없음)
		public static AiBrain CreateForHero(UnitBase hero)
		{
			return new AiBrain(hero, new HeroAutoBehavior());
		}
	}
}
