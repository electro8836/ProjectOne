using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.UI
{
	// 메인 HUD 의 MenuButton 으로 여는 메뉴 팝업. UIManager.ShowMenuPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 조회할 데이터가 없어 Presenter 를 두지 않는다.
	public class MenuPopup : UIScreen
	{
		[Header("닫기")]
		[SerializeField] private UIButton _dimButton;	// Dimmed

		[Header("메뉴")]
		[SerializeField] private UIButton _buttonSetting;
		[SerializeField] private UIButton _buttonMail;
		[SerializeField] private UIButton _buttonAfk;
		[SerializeField] private UIButton _buttonQuit;

		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dimmed 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
			_dimButton.OnClickEvent += onCloseClicked;

			_buttonSetting.OnClickEvent += onSettingClicked;
			_buttonMail.OnClickEvent += onMailClicked;
			_buttonAfk.OnClickEvent += onAfkClicked;
			_buttonQuit.OnClickEvent += onQuitClicked;
		}

		private void OnDestroy()
		{
			_dimButton.OnClickEvent -= onCloseClicked;

			_buttonSetting.OnClickEvent -= onSettingClicked;
			_buttonMail.OnClickEvent -= onMailClicked;
			_buttonAfk.OnClickEvent -= onAfkClicked;
			_buttonQuit.OnClickEvent -= onQuitClicked;
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
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

		// 아래 4개는 정식 기능 연결 전까지의 임시 처리다. 기능이 준비되면 교체한다.
		private void onSettingClicked()
		{
			Debug.Log("[MenuPopup] 설정 기능 준비 중");
		}

		private void onMailClicked()
		{
			Debug.Log("[MenuPopup] 메일 기능 준비 중");
		}

		private void onAfkClicked()
		{
			Debug.Log("[MenuPopup] 방치모드 기능 준비 중");
		}

		private void onQuitClicked()
		{
			Debug.Log("[MenuPopup] 종료 기능 준비 중");
		}
	}
}
