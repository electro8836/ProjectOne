namespace ProjectOne.UI
{
	// 출석 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct DailyBonusSlotData
	{
		public int dayCount;
		public int rewardGroupId;
		public bool isClaimed;		// 이미 받았는가 — Check
		public bool isFocused;		// 오늘의 칸인가 — Focus (받고 나서도 켜진 채로 남는다)
		public bool isClaimable;	// 지금 누르면 수령되는가 — Focus 와 별개다
	}
}
