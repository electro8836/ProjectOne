namespace ProjectOne.UI
{
	// 공용 팝업이 어떤 버튼으로 닫혔는가.
	// 닫기(ExitButton·Dimmed)는 "아무것도 고르지 않았다" 이므로 버튼과 구분한다.
	public enum CommonPopupResult
	{
		Closed = 0,
		Button1,
		Button2
	}

	// 공용 팝업 1회 표시 내용. 무엇을 묻고 어떤 선택지를 줄지는 전적으로 호출부가 정한다.
	// button2Text 가 비어 있으면 Button_2 를 끄고 1버튼 팝업(알림·확인)이 된다.
	// isDanger 는 되돌릴 수 없는 확인(분해·삭제) — Button_2 를 빨강으로 칠한다.
	public struct CommonPopupData
	{
		public string title;
		public string desc;
		public string button1Text;
		public string button2Text;
		public bool isDanger;
	}
}
