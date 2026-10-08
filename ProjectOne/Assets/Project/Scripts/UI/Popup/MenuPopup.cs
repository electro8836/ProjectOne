using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Mail;

namespace ProjectOne.UI
{
	// 메뉴 팝업이 무엇으로 닫혔는지. 메뉴는 닫히면서 다른 팝업으로 넘어가므로, 다음에 띄울 것을 호출부가 정한다.
	public enum MenuPopupResult
	{
		None = 0,
		Mail,
		Quit,
		Account
	}

	// 메인 HUD 의 MenuButton 으로 여는 메뉴 팝업. UIManager.ShowMenuPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 메일 배지 한 줄만 조회해 Presenter 를 두지 않는다.
	public class MenuPopup : UIScreen
	{
		[Header("닫기")]
		[SerializeField] private UIButton _dimButton;	// Dimmed

		[Header("메뉴")]
		[SerializeField] private UIButton _buttonSetting;
		[SerializeField] private UIButton _buttonAccount;
		[SerializeField] private UIButton _buttonMail;
		[SerializeField] private GameObject _mailBadge;	// Button_Mail/Badge — 미확인 메일이 있으면 켠다
		[SerializeField] private UIButton _buttonAfk;
		[SerializeField] private UIButton _buttonQuit;

		private UniTaskCompletionSource _tcs;
		private MenuPopupResult _result;

		private void Awake()
		{
			// 조회가 끝나기 전 프리펩 기본값이 비치지 않도록 끈 채로 시작한다.
			_mailBadge.SetActive(false);

			// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dimmed 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
			_dimButton.OnClickEvent += onCloseClicked;

			_buttonSetting.OnClickEvent += onSettingClicked;
			_buttonAccount.OnClickEvent += onAccountClicked;
			_buttonMail.OnClickEvent += onMailClicked;
			_buttonAfk.OnClickEvent += onAfkClicked;
			_buttonQuit.OnClickEvent += onQuitClicked;
		}

		private void OnDestroy()
		{
			_dimButton.OnClickEvent -= onCloseClicked;

			_buttonSetting.OnClickEvent -= onSettingClicked;
			_buttonAccount.OnClickEvent -= onAccountClicked;
			_buttonMail.OnClickEvent -= onMailClicked;
			_buttonAfk.OnClickEvent -= onAfkClicked;
			_buttonQuit.OnClickEvent -= onQuitClicked;
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask<MenuPopupResult> ShowAsync(CancellationToken ct)
		{
			_result = MenuPopupResult.None;
			_tcs = new UniTaskCompletionSource();

			// 열려 있는 동안에는 메일 상태가 바뀌지 않는다 — 열 때 한 번만 본다.
			bool hasUnread = await MailSystem.HasUnreadAsync(ct);
			_mailBadge.SetActive(hasUnread);

			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			return _result;
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		private void onCloseClicked()
		{
			Close();
		}

		private void onMailClicked()
		{
			_result = MenuPopupResult.Mail;
			Close();
		}

		// 확인 팝업과 실제 종료는 호출부(MainHudPresenter)가 처리한다.
		private void onQuitClicked()
		{
			_result = MenuPopupResult.Quit;
			Close();
		}

		private void onAccountClicked()
		{
			_result = MenuPopupResult.Account;
			Close();
		}

		// 정식 기능 연결 전까지의 임시 처리다. 기능이 준비되면 교체한다.
		private void onSettingClicked()
		{
			Debug.Log("[MenuPopup] 설정 기능 준비 중");
		}

		// 정식 기능 연결 전까지의 임시 처리다. 기능이 준비되면 교체한다.
		private void onAfkClicked()
		{
			Debug.Log("[MenuPopup] 방치모드 기능 준비 중");
		}
	}
}
