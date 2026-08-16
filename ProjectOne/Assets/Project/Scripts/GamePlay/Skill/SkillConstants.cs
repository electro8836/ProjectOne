namespace ProjectOne.Skill
{
	// 스킬 시스템의 코드 상수 (설계 15.2).
	// 밸런싱 대상이 아니라 구조가 요구하는 값이므로 테이블에 두지 않는다.
	public static class SkillConstants
	{
		// 애니메이션 재생 속도 클램프. Stat_AtkSpeed 의 Min/Max 와 같은 범위로 맞춰 둔다.
		public const float ANIM_SPEED_MIN = 0.1f;
		public const float ANIM_SPEED_MAX = 5.0f;

		// OnLowHP 스킬의 체력 검사 주기(초)
		public const float LOWHP_CHECK_INTERVAL = 0.5f;

		// ChainEffectIDs 순환 참조 방지 깊이
		public const int CHAIN_DEPTH_LIMIT = 5;
	}
}
