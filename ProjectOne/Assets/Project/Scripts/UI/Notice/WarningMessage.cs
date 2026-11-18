using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 제한 메시지(UIPrefab_WarningMessage, UI Toolkit). 레이아웃은 UIToolkit/HUD/Notice/WarningMessage.uxml.
	// Hide 를 부를 때까지 떠 있다. 상자 크기는 글자에 맞춰 늘고(최대 폭을 넘으면 줄바꿈) USS 가 정한다.
	public class WarningMessage : ToolkitScreen
	{
		private VisualElement _box;
		private Label _text;

		protected override void OnBind(VisualElement root)
		{
			_box = root.Q("warning");
			_text = root.Q<Label>("text");
			_box.style.display = DisplayStyle.None;
		}

		public void Show(string message)
		{
			_text.text = message;
			_box.style.display = DisplayStyle.Flex;
		}

		public void Hide()
		{
			_box.style.display = DisplayStyle.None;
		}
	}
}
