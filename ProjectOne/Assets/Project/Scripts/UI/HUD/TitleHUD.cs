using UnityEngine;
using ProjectOne.Network;

namespace ProjectOne.UI
{
	// 타이틀 화면 HUD — 로그인 버튼(게스트/구글) 관리.
	// 버튼 클릭 → 로그인만 수행한다. 성공 시 NetworkManager.IsLoggedIn 이 true 가 되고,
	// 다음 상태 전이는 TitleState 가 그 값을 감지해 처리한다(이 HUD 는 흐름을 모른다).
	// (Apple/Facebook 버튼은 추후 같은 패턴으로 추가)
	public class TitleHUD : UIScreen
	{
		[Header("로그인 버튼")]
		[SerializeField] private UIButton _buttonGuest;
		[SerializeField] private UIButton _buttonGoogle;

		private void Awake()
		{
			if (_buttonGuest != null)
			{
				_buttonGuest.OnClickEvent += onGuestClicked;
			}

			if (_buttonGoogle != null)
			{
				_buttonGoogle.OnClickEvent += onGoogleClicked;
			}
		}

		private void OnDestroy()
		{
			if (_buttonGuest != null)
			{
				_buttonGuest.OnClickEvent -= onGuestClicked;
			}

			if (_buttonGoogle != null)
			{
				_buttonGoogle.OnClickEvent -= onGoogleClicked;
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

			if (_buttonGoogle != null)
			{
				_buttonGoogle.interactable = value;
			}
		}
	}
}
