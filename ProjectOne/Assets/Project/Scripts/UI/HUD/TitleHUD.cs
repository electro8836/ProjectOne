using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using ProjectOne.Network;

namespace ProjectOne.UI
{
	// 타이틀 화면 HUD(UI Toolkit) — 로그인 버튼(게스트/애플/구글/디스코드) 관리. 레이아웃은 UIToolkit/Screens/Title/TitleScreen.uxml.
	// 2.Title 씬에 놓여 있고, Start 에서 UIManager 의 HUD 층에 스스로 붙는다(씬이 내려가면 OnDestroy 가 뗀다).
	// 연동 종류는 계정 팝업과 같다.
	// 버튼 클릭 → 로그인만 수행한다. 성공 시 NetworkManager.IsLoggedIn 이 true 가 되고,
	// 다음 상태 전이는 TitleState 가 그 값을 감지해 처리한다(이 HUD 는 흐름을 모른다).
	// 애플·디스코드는 로그인 핸들러가 붙기 전까지 누르면 "준비 중" 알림만 띄운다. 애플은 iOS 에서만 보인다.
	public class TitleHUD : ToolkitScreen
	{
		private const string ComingSoon = "준비 중입니다";

		private Button _buttonGuest;
		private Button _buttonApple;
		private Button _buttonGoogle;
		private Button _buttonDiscord;

		private void Start()
		{
			UIManager.Instance.AttachToHudLayer(this);
		}

		protected override void OnBind(VisualElement root)
		{
			_buttonGuest = root.Q<Button>("guest");
			_buttonApple = root.Q<Button>("apple");
			_buttonGoogle = root.Q<Button>("google");
			_buttonDiscord = root.Q<Button>("discord");

			_buttonGuest.clicked += onGuestClicked;
			_buttonApple.clicked += onComingSoonClicked;
			_buttonGoogle.clicked += onGoogleClicked;
			_buttonDiscord.clicked += onComingSoonClicked;

#if !UNITY_IOS
			_buttonApple.style.display = DisplayStyle.None;
#endif
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_buttonGuest.clicked -= onGuestClicked;
				_buttonApple.clicked -= onComingSoonClicked;
				_buttonGoogle.clicked -= onGoogleClicked;
				_buttonDiscord.clicked -= onComingSoonClicked;
			}

			base.OnDestroy();
		}

		// 게스트(디바이스) 로그인.
		private void onGuestClicked()
		{
			setInteractable(false);
			NetworkManager.Instance.Login(LoginType.Guest, onLoginResult);
		}

		// 구글 로그인 — 계정 선택 UI 가 떠서 비동기.
		private void onGoogleClicked()
		{
			setInteractable(false);
			NetworkManager.Instance.Login(LoginType.Google, onLoginResult);
		}

		// 애플·디스코드 — 로그인 핸들러가 아직 없다.
		private void onComingSoonClicked()
		{
			showComingSoonAsync(destroyCancellationToken).Forget();
		}

		// 알림 메시지는 마을 진입 때 만들어진다 — 타이틀에선 아직 없을 수 있어 먼저 세운다(이미 있으면 그대로).
		private async UniTaskVoid showComingSoonAsync(CancellationToken ct)
		{
			await UIManager.Instance.EnsureNoticeMessagesAsync(ct);
			UIManager.Instance.ShowAlertMessage(ComingSoon);
		}

		// 로그인 결과 — 실패 시 버튼 재활성화. 성공 시 TitleState 가 IsLoggedIn 을 감지해 전이.
		private void onLoginResult(bool isSuccess, string errorMsg)
		{
			if (isSuccess == false)
			{
				setInteractable(true);
			}
		}

		// 로그인 시도 중 중복 클릭 방지.
		private void setInteractable(bool value)
		{
			_buttonGuest.SetEnabled(value);
			_buttonApple.SetEnabled(value);
			_buttonGoogle.SetEnabled(value);
			_buttonDiscord.SetEnabled(value);
		}
	}
}
