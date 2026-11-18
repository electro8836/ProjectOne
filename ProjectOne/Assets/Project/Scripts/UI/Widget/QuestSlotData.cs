namespace ProjectOne.UI
{
	// 퀘스트 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct QuestSlotData
	{
		public int questId;
		public string name;
		public string desc;
		public bool isCurrent;		// 지금 진행 중인 퀘스트인가 — 틀 강조
		public bool isCleared;		// 이미 깨서 보상을 받았는가 — Clear / Check(Icon) 표시
		public bool isClaimable;	// 목표는 달성했지만 UI 완료형이라 아직 안 받았는가 — Check(ClaimText) 표시
		public int rewardGroupId;
	}
}
