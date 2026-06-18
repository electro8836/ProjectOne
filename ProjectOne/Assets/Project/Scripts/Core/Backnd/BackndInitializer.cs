using Cysharp.Threading.Tasks;
using UnityEngine;
using BackEnd;
using Google;

namespace ProjectOne.ServerData
{
	// 뒤끝(Backnd) 초기화 + 디바이스(게스트) 로그인.
	// 로딩 단계(LoginState)에서 1회 동기 호출. 기기 고유 ID 기반이라 자격증명 없이 접속된다.
	// 초기화는 로컬(키 검증)이라 빠르고, 게스트 로그인만 네트워크다 — 로딩 화면 1회라 동기 허용.
	// 추후: 소셜 로그인 도입 시 guestLogin 부만 교체. 비동기화 필요하면 SendQueue 사용.
	public static class BackndInitializer
	{
		// ── 설정 상수 ── (콘솔/OAuth 설정 후 값만 교체)
		// 구글 웹 애플리케이션 OAuth 클라이언트 ID (공개값). Android/iOS 타입 아님 — idToken audience 용.
		private const string WebClientId = "627792620661-jhtuk5dbshbsb2k6bson3586l2khsnhf.apps.googleusercontent.com";
		// 페데레이션 customParam — Backnd 콘솔 식별용 문자열.
		private const string GoogleFederationParam = "google";
		// (추후) Apple: private const string AppleFederationParam = "apple";  // Service ID 등은 콘솔 설정
		// (추후) Facebook: private const string FacebookAppId = "...";       private const string FacebookFederationParam = "facebook";

		private static bool _initialized;
		private static bool _googleConfigured;

		// 현재 로그인 성공 상태 — 실패해도 게임은 로컬 데이터로 진행 가능.
		public static bool IsLoggedIn { get; private set; }

		// 초기화(1회) 후 게스트 로그인까지 수행. 성공 여부 반환.
		public static bool InitAndGuestLogin()
		{
			if (initialize() == false)
			{
				return false;
			}

			return guestLogin();
		}

		// 뒤끝 SDK 초기화 — TheBackendSettings 의 키로 로컬 초기화(네트워크 아님).
		private static bool initialize()
		{
			if (_initialized == true)
			{
				return true;
			}

			BackendReturnObject bro = Backend.Initialize();
			if (bro.IsSuccess() == false)
			{
				Debug.LogError($"[Backnd] 초기화 실패: {bro.GetMessage()}");
				return false;
			}

			_initialized = true;
			Debug.Log("[Backnd] 초기화 성공");
			return true;
		}

		// 디바이스(게스트) 로그인 — 기기 고유 ID 기반 계정. 최초 호출 시 계정 자동 생성.
		private static bool guestLogin()
		{
			BackendReturnObject bro = Backend.BMember.GuestLogin();
			if (bro.IsSuccess() == false)
			{
				IsLoggedIn = false;
				Debug.LogError($"[Backnd] 게스트 로그인 실패: {bro.GetMessage()}");
				return false;
			}

			onLoginSucceeded();
			Debug.Log($"[Backnd] 게스트 로그인 성공 - user: {Backend.UserInDate}");
			return true;
		}

		// 로그인 성공 공통 처리 — 로그인 상태 표시 + 명령 계층을 Backnd 로 교체.
		// 서버 권위: 읽기/쓰기 모두 뒤끝 함수 경유(클라 직접 저장 없음).
		private static void onLoginSucceeded()
		{
			IsLoggedIn = true;
			ServerCommandSystem.SetCommand(new BackndServerCommand());
		}

		// 구글 로그인 — 로그인 화면의 "구글 로그인" 버튼에서 호출(사용자 동작 기반).
		// GoogleSignIn 으로 idToken 을 받아 뒤끝 페데레이션 인증으로 넘긴다.
		public static async UniTask<bool> GoogleLoginAsync()
		{
			if (initialize() == false)
			{
				return false;
			}

			string idToken = await getGoogleIdTokenAsync();
			if (string.IsNullOrEmpty(idToken) == true)
			{
				Debug.LogError("[Backnd] 구글 idToken 획득 실패");
				return false;
			}

			return federationLogin(idToken);
		}

		// GoogleSignIn 설정 — WebClientId(웹 타입) 로 idToken 요청. 인스턴스 생성 전 1회만 설정 가능.
		private static void configureGoogle()
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
		private static async UniTask<string> getGoogleIdTokenAsync()
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
		private static bool federationLogin(string idToken)
		{
			// 설치된 SDK 버전이 AuthorizeFederationV2(token, type, customParam) 라면 이 줄만 교체.
			BackendReturnObject bro = Backend.BMember.AuthorizeFederation(idToken, FederationType.Google, GoogleFederationParam);
			if (bro.IsSuccess() == false)
			{
				IsLoggedIn = false;
				Debug.LogError($"[Backnd] 구글 페데레이션 로그인 실패: {bro.GetMessage()}");
				return false;
			}

			onLoginSucceeded();
			Debug.Log($"[Backnd] 구글 로그인 성공 - user: {Backend.UserInDate}");
			return true;
		}
	}
}
