namespace ProjectOne.UI
{
	// 필드 슬롯 1칸 렌더 데이터. Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct FieldSlotData
	{
		public int fieldId;
		public int order;		// 액트 안에서의 순번 — Text_Num 에 그대로 쓴다
		public string name;
		public bool cleared;	// 이동 가능한가 (ReqQuestID 클리어)
		public bool current;	// 지금 서 있는 필드인가
	}
}
