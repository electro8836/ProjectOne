using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;

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
	public struct CommonPopupData
	{
		public string title;
		public string desc;
		public string button1Text;
		public string button2Text;
	}

	// 공용 확인 팝업(UIPrefab_CommonPopup). UIManager.ShowCommonPopupAsync 가 결과를 기다린다.
	//
	// 다른 팝업과 달리 Presenter 가 없다 — 조회할 Model 없이 호출부가 넘긴 문자열을 그리고
	// 어느 버튼이 눌렸는지만 돌려주므로 Presenter 를 두면 빈 껍데기가 된다.
	public class CommonPopup : UIScreen, IView
	{
		[Header("텍스트")]
		[SerializeField] private TMP_Text _titleText;	// Frame/TitleText
		[SerializeField] private TMP_Text _descText;	// Frame/DescText

		[Header("버튼")]
		[SerializeField] private UIButton _button1;			// Frame/BottomButtons/Button_1
		[SerializeField] private UIButton _button2;			// Frame/BottomButtons/Button_2
		[SerializeField] private TMP_Text _button1Text;	// Frame/BottomButtons/Button_1/Text (TMP)
		[SerializeField] private TMP_Text _button2Text;	// Frame/BottomButtons/Button_2/Text (TMP)

		// 닫기는 ExitButton 과 Dimmed 두 경로다. Frame 은 Bg 가 레이캐스트를 흡수하므로
		// Dimmed 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
		[SerializeField] private UIButton _exitButton;		// Frame/ExitButton
		[SerializeField] private UIButton _dimmedButton;	// Dimmed

		private UniTaskCompletionSource<CommonPopupResult> _tcs;

		private void Awake()
		{
			_button1.OnClickEvent += onButton1Clicked;
			_button2.OnClickEvent += onButton2Clicked;
			_exitButton.OnClickEvent += onCloseClicked;
			_dimmedButton.OnClickEvent += onCloseClicked;
		}

		private void OnDestroy()
		{
			_button1.OnClickEvent -= onButton1Clicked;
			_button2.OnClickEvent -= onButton2Clicked;
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimmedButton.OnClickEvent -= onCloseClicked;
		}

		// UIManager 가 인스턴스화 직후 호출해 어떤 버튼으로 닫혔는지 받는다.
		// 아이콘 로드 같은 비동기가 없어 첫 프레임 전에 내용이 다 채워진다 — 숨김/Reveal 이 필요 없다.
		public async UniTask<CommonPopupResult> ShowAsync(CommonPopupData data, CancellationToken ct)
		{
			_titleText.text = data.title;
			_descText.text = data.desc;
			_button1Text.text = data.button1Text;

			// 두 번째 선택지가 없으면 버튼 자체를 끈다. GridLayoutGroup 은 활성 자식 수로 정렬을
			// 계산하므로, 끄기만 하면 Button_1 이 가운데로 온다.
			bool hasButton2 = string.IsNullOrEmpty(data.button2Text) == false;
			_button2.gameObject.SetActive(hasButton2);
			if (hasButton2 == true)
			{
				_button2Text.text = data.button2Text;
			}

			_tcs = new UniTaskCompletionSource<CommonPopupResult>();

			(bool cancelled, CommonPopupResult result) = await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return CommonPopupResult.Closed;
			}

			return result;
		}

		// ── 내부: 입력 → 결과 확정 ─────────────────────────────────────────

		private void onButton1Clicked()
		{
			close(CommonPopupResult.Button1);
		}

		private void onButton2Clicked()
		{
			close(CommonPopupResult.Button2);
		}

		private void onCloseClicked()
		{
			close(CommonPopupResult.Closed);
		}

		private void close(CommonPopupResult result)
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(result);
			}
		}
	}
}
