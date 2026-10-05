using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;
using ProjectOne.UI;

namespace ProjectOne.Network
{
	// 유저가 직접 누른 요청(딤을 띄우는 요청)의 실패를 화면에 알린다. BackndFunctionCaller 가 부른다.
	//
	//  - 거절: 서버가 사유를 달아 돌려줬다. 서버 상태는 바뀌지 않았으므로 경고 메시지로 안내만 한다.
	//  - 통신 실패: 호출 쪽이 같은 요청을 스스로 다시 보낸다. 그동안 게임을 멈추고 재연결 문구를 띄운다.
	//    다시 보내기까지 모두 실패하면 타이틀로 돌려보내고 거기서 안내 팝업을 띄운다.
	//
	// 백그라운드 저장(장착·필드 정산 등)은 각자 재전송·되돌림을 하므로 여기로 오지 않는다.
	public static class NetworkFailureHandler
	{
		// 다시 보내는 중인 요청 수 — 0 이 되면 재연결 표시를 걷는다.
		private static int _retryCount;

		// 재연결 표시를 켜기 직전의 게임 속도. 계속하기 팝업처럼 이미 멈춰 있던 상태일 수 있어 그대로 되돌린다.
		private static float _previousTimeScale = 1f;

		private static bool _goingToTitle;

		public static void OnRejected(string action, string error)
		{
			if (_goingToTitle == true || UIManager.HasInstance == false)
			{
				return;
			}

			UIManager.Instance.ShowAlertMessage(NetworkMessages.ForRejected(error));
		}

		// 다시 보내기 시작 — 게임을 멈추고 재연결 문구를 띄운다.
		public static void BeginRetry()
		{
			_retryCount++;
			if (_retryCount > 1)
			{
				return;
			}

			_previousTimeScale = Time.timeScale;
			Time.timeScale = 0f;

			if (UIManager.HasInstance == true)
			{
				UIManager.Instance.ShowNetworkMessage(NetworkMessages.Reconnecting);
			}
		}

		// 다시 보내기 끝(성공·거절·포기 모두) — 문구를 걷고 속도를 되돌린다.
		public static void EndRetry()
		{
			if (_retryCount <= 0)
			{
				return;
			}

			_retryCount--;
			if (_retryCount > 0)
			{
				return;
			}

			if (UIManager.HasInstance == true)
			{
				UIManager.Instance.HideNetworkMessage();
			}

			Time.timeScale = _previousTimeScale;
		}

		// 다시 보내기를 다 써도 닿지 않았다 — 타이틀로 돌려보낸다.
		public static void OnRetryExhausted()
		{
			if (_goingToTitle == true)
			{
				return;
			}

			_goingToTitle = true;
			goToTitleAsync().Forget();
		}

		private static async UniTaskVoid goToTitleAsync()
		{
			// 타이틀과 재로그인이 멈춘 채 남지 않게 한다.
			Time.timeScale = 1f;

			await DataLoadState.GoToTitleAsync(NetworkMessages.ConnectionFailed);
			_goingToTitle = false;
		}
	}
}
