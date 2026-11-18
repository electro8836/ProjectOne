using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.UI
{
	// 결과가 없는 팝업의 TResult.
	public readonly struct NoResult
	{
	}

	// UI Toolkit 팝업의 베이스 (UI구조_설계.md 4.1). UIManager.ShowPopupAsync 가 열고 결과를 기다린다.
	//
	// 레이아웃 최상위에 .popup 요소(딤 + 창)를 둔다. 열 때 ToolkitScreen.PlayPopupOpen 이 등장 전환을 건다.
	public abstract class UIPopup<TArgs, TResult> : ToolkitScreen
	{
		private UniTaskCompletionSource<TResult> _tcs;

		public async UniTask<TResult> ShowAsync(TArgs args, CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<TResult>();
			OnShow(args);
			PlayPopupOpen();

			(bool cancelled, TResult result) = await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return CancelledResult;
			}

			return result;
		}

		// 내용을 채운다 — 비동기 로드(아이콘 등)는 Forget 으로 걸고 기다리지 않는다.
		protected abstract void OnShow(TArgs args);

		// 바깥 취소(씬 전환·같은 팝업 재요청)로 닫혔을 때 돌려줄 값.
		protected virtual TResult CancelledResult
		{
			get { return default; }
		}

		// 결과를 확정한다. 이미 확정됐으면 무시한다(연타 방지).
		protected void Close(TResult result)
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(result);
			}
		}
	}
}
