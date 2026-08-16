namespace ProjectOne.Unit
{
	// Hero 후처리 단계 — 적용 순서를 enum 으로 고정 (수동 ApplyOrder int 사용 금지)
	public enum HeroAspectStage
	{
		Base = 0,         // (예약) UnitFactory 가 BaseStat 주입에 이미 사용 — Aspect로는 쓰지 않음
		Enhance = 10,     // 캐릭터 강화
		Achievement = 20, // 업적 보너스
		Collection = 30,  // 콜렉션(스킨 보유 등) 효과
		Equipment = 40,   // 장비 장착
		Skill = 50,           // 외부 스킬 부여
		Buff = 60         // 영구성 버프
	}

	// Hero 에 자신의 효과(스탯/스킬)를 push/remove 할 수 있는 출처(source) 모듈
	// - SourceKey 는 StatContainer/SkillContainer 의 source 태그와 동일하게 사용 (예: "Equipment")
	public interface IHeroAspect
	{
		HeroAspectStage Stage { get; }
		string SourceKey { get; }

		void ApplyTo(Hero hero);
		void RemoveFrom(Hero hero);
	}
}
