namespace ProjectOne.Skill
{
	// 스킬 시스템의 코드 상수 (설계 15.2).
	// 밸런싱 대상이 아니라 구조가 요구하는 값이므로 테이블에 두지 않는다.
	public static class SkillConstants
	{
		// 애니메이션 재생 속도 클램프. Stat_AtkSpeed 의 Min/Max(상한 증가 포함)를 덮는 범위로 둔다.
		public const float ANIM_SPEED_MIN = 0.1f;
		public const float ANIM_SPEED_MAX = 5.0f;

		// OnLowHP 스킬의 체력 검사 주기(초)
		public const float LOWHP_CHECK_INTERVAL = 0.5f;

		// ChainEffectIDs 순환 참조 방지 깊이
		public const int CHAIN_DEPTH_LIMIT = 5;

		// 출혈 — 무기와 무관한 공통 상태이상. 출혈 전용 스탯(최대 중첩·피해량·중첩당 평타 피해)이 이 버프를 가리킨다.
		public const EDT.Buff BLEED_BUFF = EDT.Buff.BUFF_Bleed;

		// 체력 비율이 이 값 이하인 대상에게 Stat_LowHpTargetDamageBonus 가 붙는다 (단검 처형자)
		public const float LOW_HP_TARGET_RATIO = 0.5f;
	}
}
