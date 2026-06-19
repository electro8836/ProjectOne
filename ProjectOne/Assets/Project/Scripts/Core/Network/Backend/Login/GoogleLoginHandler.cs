using Cysharp.Threading.Tasks;
using UnityEngine;
using BackEnd;
using Google;

namespace ProjectOne.Network
{
	// 구글 로그인 — GoogleSignIn 으로 idToken 을 받아 뒤끝 페데레이션 인증으로 넘긴다.
	public sealed class GoogleLoginHandler : ILoginHandler
	{
		// ── 설정 상수 ── (콘솔/OAuth 설정 후 값만 교체)
		// 구글 웹 애플리케이션 OAuth 클라이언트 ID (공개값). Android/iOS 타입 아님 — idToken audience 용.
		private const string WebClientId = "627792620661-jhtuk5dbshbsb2k6bson3586l2khsnhf.apps.googleusercontent.com";
		// 페데레이션 customParam — Backnd 콘솔 식별용 문자열.
		private const string GoogleFederationParam = "google";

		// GoogleSignIn 설정은 인스턴스 생성 전 1회만 가능 — 핸들러 간 공유를 위해 static.
		private static bool _googleConfigured;

		public async UniTask<(bool success, string error)> LoginAsync()
		{
			string idToken = await getGoogleIdTokenAsync();
			if (string.IsNullOrEmpty(idToken) == true)
			{
				return (false, "구글 idToken 획득 실패");
			}

			return federationLogin(idToken);
		}

		// GoogleSignIn 설정 — WebClientId(웹 타입) 로 idToken 요청. 인스턴스 생성 전 1회만 설정 가능.
		private void configureGoogle()
		{
			if (_googleConfigured == true)
			{
				return;
			}

			GoogleSignIn.Configuration = new GoogleSignInConfiguration
			{
				WebClientId = WebClientId,
				RequestIdToken = true,
				RequestEmail = true,
				UseGameSignIn = false
			};
			_googleConfigured = true;
		}

		// 구글 로그인 UI → idToken. 취소/실패는 플러그인 경계 예외로 전달되므로 여기서만 try-catch 허용.
		private async UniTask<string> getGoogleIdTokenAsync()
		{
			configureGoogle();

			string idToken = null;
			try
			{
				GoogleSignInUser user = await GoogleSignIn.DefaultInstance.SignIn().AsUniTask();
				idToken = (user != null) ? user.IdToken : null;
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[Backnd] 구글 로그인 취소/실패: {e.Message}");
				idToken = null;
			}

			// SignIn 콜백이 워커 스레드에서 재개될 수 있으므로 메인 스레드로 복귀.
			await UniTask.SwitchToMainThread();
			return idToken;
		}

		// 뒤끝 페데레이션 인증 — idToken 을 구글 토큰으로 검증.
		private (bool success, string error) federationLogin(string idToken)
		{
			// 설치된 SDK 버전이 AuthorizeFederationV2(token, type, customParam) 라면 이 줄만 교체.
			BackendReturnObject bro = Backend.BMember.AuthorizeFederation(idToken, FederationType.Google, GoogleFederationParam);
			if (bro.IsSuccess() == false)
			{
				return (false, bro.GetMessage());
			}

			return (true, null);
		}
	}
}
