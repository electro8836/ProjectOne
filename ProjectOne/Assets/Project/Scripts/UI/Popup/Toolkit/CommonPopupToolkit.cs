using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 공용 확인 팝업의 UI Toolkit 판 (UIPrefab_CommonPopupToolkit). 레이아웃은 UIToolkit/Popups/Common/CommonPopup.uxml.
	// UIManager.ShowCommonPopupAsync 가 ShowPopupAsync 로 열고 어떤 버튼으로 닫혔는지 받는다.
	//
	// Presenter 가 없다 — 조회할 Model 없이 호출부가 넘긴 문자열을 그리고 누른 버튼만 돌려준다.
	public class CommonPopupToolkit : UIPopup<CommonPopupData, CommonPopupResult>
	{
		private Label _title;
		private Label _desc;
		private Button _button1;
		private Button _button2;

		protected override void OnBind(VisualElement root)
		{
			_title = root.Q<Label>("title");
			_desc = root.Q<Label>("desc");
			_button1 = root.Q<Button>("button-1");
			_button2 = root.Q<Button>("button-2");

			_button1.clicked += onButton1Clicked;
			_button2.clicked += onButton2Clicked;
			root.Q<Button>("close").clicked += onCloseClicked;

			// 딤(팝업 밖)을 누르면 아무것도 고르지 않은 닫기다.
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
		}

		protected override void OnShow(CommonPopupData data)
		{
			_title.text = data.title;
			_desc.text = data.desc;
			_button1.text = data.button1Text;

			// 두 번째 선택지가 없으면 버튼을 끈다 — 남은 버튼이 줄 전체를 차지한다.
			bool hasButton2 = string.IsNullOrEmpty(data.button2Text) == false;
			_button2.style.display = hasButton2 ? DisplayStyle.Flex : DisplayStyle.None;
			if (hasButton2 == true)
			{
				_button2.text = data.button2Text;
			}
		}

		protected override CommonPopupResult CancelledResult
		{
			get { return CommonPopupResult.Closed; }
		}

		private void onButton1Clicked()
		{
			Close(CommonPopupResult.Button1);
		}

		private void onButton2Clicked()
		{
			Close(CommonPopupResult.Button2);
		}

		private void onCloseClicked()
		{
			Close(CommonPopupResult.Closed);
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close(CommonPopupResult.Closed);
		}
	}
}
