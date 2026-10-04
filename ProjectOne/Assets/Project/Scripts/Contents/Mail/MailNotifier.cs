using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using BackEnd;
using BackEnd.Socketio;

namespace ProjectOne.Mail
{
	// 새 우편 실시간 알림 — 뒤끝 실시간 알림(콘솔에서 활성화 필요)의 OnNewPostCreated 로 감지한다.
	//
	// 알림이 오면 메일 목록을 서버에서 다시 받는다. 목록을 받으면 BackndMailProvider 가 MailChangedEvent 를 내
	// 메뉴 버튼 배지가 바뀐다. 알림 콜백 스레드는 문서에 명시가 없어 메인 스레드로 넘겨서 처리한다.
	public static class MailNotifier
	{
		private static bool _connected;

		// 로그인 후 1회. 끊기면 SDK 가 스스로 재연결한다.
		public static void Connect()
		{
			if (_connected == true)
			{
				return;
			}

			_connected = true;
			Backend.Notification.OnAuthorize = onAuthorize;
			Backend.Notification.OnNewPostCreated = onNewPostCreated;
			Backend.Notification.OnDisConnect = onDisconnect;
			Backend.Notification.OnReconnectFailed = onReconnectFailed;
			Backend.Notification.Connect();
		}

		private static void onAuthorize(bool result, string reason)
		{
			if (result == true)
			{
				Debug.Log("[MailNotifier] 실시간 알림 연결");
				return;
			}

			Debug.LogWarning($"[MailNotifier] 실시간 알림 연결 실패 — 콘솔에서 실시간 알림이 켜져 있는지 확인: {reason}");
		}

		private static void onNewPostCreated(PostRepeatType postRepeatType, string title, string content, string author)
		{
			refreshAsync().Forget();
		}

		private static void onDisconnect(string reason)
		{
			Debug.LogWarning($"[MailNotifier] 실시간 알림 끊김: {reason}");
		}

		private static void onReconnectFailed()
		{
			Debug.LogWarning("[MailNotifier] 실시간 알림 재연결 실패 — 새 우편은 메뉴·메일함을 열 때 반영된다");
		}

		private static async UniTaskVoid refreshAsync()
		{
			await UniTask.SwitchToMainThread();

			MailSystem.Provider.MarkStale();
			await MailSystem.HasUnreadAsync(CancellationToken.None);
		}
	}
}
