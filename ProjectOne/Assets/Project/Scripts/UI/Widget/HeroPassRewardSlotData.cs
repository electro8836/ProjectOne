namespace ProjectOne.UI
{
	// 히어로패스 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct HeroPassRewardSlotData
	{
		public int level;
		public float sliderValue;			// 미도달 0 / 현재 레벨 0.5 / 지난 레벨 1
		public bool isLocked;				// 패스 미구매 — Reward_Pass 의 Lock

		public int normalGroupId;
		public bool normalClaimed;
		public bool normalClaimable;

		public int advancedGroupId;
		public bool advancedClaimed;
		public bool advancedClaimable;
	}
}
