namespace ProjectOne.UI
{
	// 현재 마스터리 정보 1회분 렌더 데이터 — Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct MasteryInfoData
	{
		public string iconAddress;
		public string name;
		public int level;
		public float expRatio;		// 현재 레벨 구간의 진행률 0~1
		public bool isMaxLevel;
	}

	// 전체 마스터리 정보 1회분 렌더 데이터.
	public struct MasteryTotalData
	{
		public int totalLevel;		// 전 마스터리 레벨 합
		public int maxTotalLevel;	// 만렙 × 마스터리 종수
		public string bonusText;	// 레벨 보너스 옵션 문구를 한 줄로 이어붙인 것
		public float ratio;			// totalLevel / maxTotalLevel
		public bool isMax;
	}

	// 전체 마스터리 목록의 슬롯 1칸 렌더 데이터.
	public struct MasteryInfoSlotData
	{
		public string iconAddress;
		public string name;
		public int level;
		public int maxLevel;
		public string bonusText;
		public bool isActive;	// 착용 무기의 마스터리
	}
}
