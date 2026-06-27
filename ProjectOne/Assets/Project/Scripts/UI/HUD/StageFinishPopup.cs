using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace ProjectOne.UI
{
	// 스테이지 종료/도전 확인 팝업. UIManager.ShowConfirmPopupAsync가 WaitResultAsync로 결과를 기다린다.
	public class StageFinishPopup : UIScreen
	{
		[SerializeField] private TextMeshProUGUI _messageText;
		[SerializeField] private UIButton _confirmButton;
		[SerializeField] private UIButton _cancelButton;

		private UniTaskCompletionSource<bool> _tcs;

		private void Awake()
		{
			_confirmButton.OnClickEvent += onConfirm;
			_cancelButton.OnClickEvent += onCancel;
		}

		private void OnDestroy()
		{
			_confirmButton.OnClickEvent -= onConfirm;
			_cancelButton.OnClickEvent -= onCancel;
		}

		// UIManager가 인스턴스화 직후 호출해 결과를 기다린다.
		public async UniTask<bool> WaitResultAsync(string message, CancellationToken ct)
		{
			_messageText.text = message;
			_tcs = new UniTaskCompletionSource<bool>();

			(bool cancelled, bool result) = await _tcs.Task
				.AttachExternalCancellation(ct)
				.SuppressCancellationThrow();

			return !cancelled && result;
		}

		private void onConfirm()
		{
			_tcs?.TrySetResult(true);
		}

		private void onCancel()
		{
			_tcs?.TrySetResult(false);
		}
	}
}
