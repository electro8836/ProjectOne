using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Network;

namespace ProjectOne.UI
{
	// 타이틀 화면 HUD — 로그인 버튼(게스트/애플/구글/디스코드) 관리. 연동 종류는 계정 팝업과 같다.
	// 버튼 클릭 → 로그인만 수행한다. 성공 시 NetworkManager.IsLoggedIn 이 true 가 되고,
	// 다음 상태 전이는 TitleState 가 그 값을 감지해 처리한다(이 HUD 는 흐름을 모른다).
	// 애플·디스코드는 로그인 핸들러가 붙기 전까지 누르면 "준비 중" 알림만 띄운다. 애플은 iOS 에서만 보인다.
	public class TitleHUD : UIScreen
	{
		private const string ComingSoon = "준비 중입니다";

		[Header("로그인 버튼")]
		[SerializeField] private UIButton _buttonGuest;
		[SerializeField] private UIButton _buttonApple;
		[SerializeField] private UIButton _buttonGoogle;
		[SerializeField] private UIButton _buttonDiscord;

		[Header("애플 버튼 줄 — iOS 가 아니면 끈다")]
		[SerializeField] private GameObject _appleRow;

		private void Awake()
		{
			if (_buttonGuest != null)
			{
				_buttonGuest.OnClickEvent += onGuestClicked;
			}

			if (_buttonApple != null)
			{
				_buttonApple.OnClickEvent += onComingSoonClicked;
			}

			if (_buttonGoogle != null)
			{
				_buttonGoogle.OnClickEvent += onGoogleClicked;
			}

			if (_buttonDiscord != null)
			{
				_buttonDiscord.OnClickEvent += onComingSoonClicked;
			}

#if !UNITY_IOS
			if (_appleRow != null)
			{
				_appleRow.SetActive(false);
			}
#endif
		}

		private void OnDestroy()
		{
			if (_buttonGuest != null)
			{
				_buttonGuest.OnClickEvent -= onGuestClicked;
			}

			if (_buttonApple != null)
			{
				_buttonApple.OnClickEvent -= onComingSoonClicked;
			}

			if (_buttonGoogle != null)
			{
				_buttonGoogle.OnClickEvent -= onGoogleClicked;
			}

			if (_buttonDiscord != null)
			{
				_buttonDiscord.OnClickEvent -= onComingSoonClicked;
			}
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
			if (_buttonGuest != null)
			{
				_buttonGuest.interactable = value;
			}

			if (_buttonApple != null)
			{
				_buttonApple.interactable = value;
			}

			if (_buttonGoogle != null)
			{
				_buttonGoogle.interactable = value;
			}

			if (_buttonDiscord != null)
			{
				_buttonDiscord.interactable = value;
			}
		}
	}
}
