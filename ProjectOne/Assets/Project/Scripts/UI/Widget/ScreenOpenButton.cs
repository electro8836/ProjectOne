using System;
using UnityEngine;

namespace ProjectOne.UI
{
	// "누르면 이 화면을 연다"를 버튼 자신이 들고 있게 하는 얇은 어댑터.
	//
	// UIButton.OnClickEvent 는 인자가 없어 구독자가 어느 버튼이 눌렸는지 알 수 없다.
	// 버튼마다 이 컴포넌트를 붙이면 자기 ID 를 실어 올려보낼 수 있고, 람다 없이 메서드 그룹으로만 연결된다.
	//
	// **여는 결정은 하지 않는다.** 어느 화면을 열지는 인스펙터가, 실제로 여는 것은 Presenter 가 한다.
	[RequireComponent(typeof(UIButton))]
	public class ScreenOpenButton : MonoBehaviour
	{
		[Tooltip("이 버튼이 여는 화면")]
		[SerializeField] private UIScreenId _screen = UIScreenId.None;

		private UIButton _button;

		public event Action<UIScreenId> OnClicked;

		public UIScreenId Screen
		{
			get { return _screen; }
		}

		private void Awake()
		{
			_button = this.GetComponent<UIButton>();
			_button.OnClickEvent += onButtonClicked;
		}

		private void OnDestroy()
		{
			if (_button != null)
			{
				_button.OnClickEvent -= onButtonClicked;
			}
		}

		private void onButtonClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(_screen);
			}
		}
	}
}
