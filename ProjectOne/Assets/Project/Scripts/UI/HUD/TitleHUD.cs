using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.ServerData;

namespace ProjectOne.UI
{
	// 타이틀 화면 HUD — 로그인 버튼(게스트/구글) 관리.
	// 버튼 클릭 → 로그인만 수행한다. 성공 시 BackndInitializer.IsLoggedIn 이 true 가 되고,
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

		// 게스트(디바이스) 로그인 — 동기.
		private void onGuestClicked()
		{
			setInteractable(false);
			bool ok = BackndInitializer.InitAndGuestLogin();
			if (ok == false)
			{
				setInteractable(true);
			}
		}

		// 구글 로그인 — 계정 선택 UI 가 떠서 비동기.
		private void onGoogleClicked()
		{
			runGoogleLoginAsync().Forget();
		}

		private async UniTaskVoid runGoogleLoginAsync()
		{
			setInteractable(false);
			bool ok = await BackndInitializer.GoogleLoginAsync();
			if (ok == false)
			{
				setInteractable(true);
			}
			// 성공 시 TitleState 가 IsLoggedIn 을 감지해 다음 상태로 전이.
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
