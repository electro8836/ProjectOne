using Cysharp.Threading.Tasks;
using UnityEngine;
using BackEnd;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.Network
{
	// 네트워크 진입점(순수 C# 싱글톤) — 뒤끝 초기화 / 로그인 / 백엔드 함수 호출을 모두 관리한다.
	// 백엔드 함수는 외부에서 직접 호출하지 않고 여기 래핑된 Request* 메서드로만 호출한다(서버 권위).
	public sealed class NetworkManager : Singleton<NetworkManager>
	{
		private readonly BackndFunctionCaller _caller = new BackndFunctionCaller();

		private bool _initialized;

		// 현재 로그인 성공 상태 — 실패해도 게임은 로컬 데이터로 진행 가능.
		public bool IsLoggedIn { get; private set; }

		private NetworkManager() { }

		// 뒤끝 SDK 초기화(1회, 멱등) — TheBackendSettings 의 키로 로컬 초기화(네트워크 아님).
		public bool Init()
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

		// 로그인 — 타입별 핸들러로 위임한다. 초기화가 안 됐으면 먼저 수행.
		public void Login(LoginType type, LoginCallback callback)
		{
			loginAsync(type, callback).Forget();
		}

		private async UniTaskVoid loginAsync(LoginType type, LoginCallback callback)
		{
			if (Init() == false)
			{
				callback?.Invoke(false, "초기화 실패");
				return;
			}

			ILoginHandler handler = createLoginHandler(type);
			if (handler == null)
			{
				callback?.Invoke(false, $"미지원 로그인 타입: {type}");
				return;
			}

			(bool success, string error) = await handler.LoginAsync();
			IsLoggedIn = success;
			if (success == true)
			{
				Debug.Log($"[Backnd] 로그인 성공({type}) - user: {Backend.UserInDate}");
			}
			else
			{
				Debug.LogError($"[Backnd] 로그인 실패({type}): {error}");
			}

			callback?.Invoke(success, error);
		}

		// 로그인 타입 → 핸들러. (Apple/Facebook 은 핸들러 추가 시 case 만 늘린다.)
		private ILoginHandler createLoginHandler(LoginType type)
		{
			switch (type)
			{
				case LoginType.Guest:
					return new GuestLoginHandler();
				case LoginType.Google:
					return new GoogleLoginHandler();
				default:
					return null;
			}
		}

		// ── 백엔드 함수 래퍼 ──────────────────────────────────────────────

		// 계정 데이터 요청 — 로그인 후 전체 계정 로드.
		public void RequestGetUserData(ResponseCallback<GetUserDataResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<GetUserDataRequest, GetUserDataResponse>(FunctionName.GetUserData, new GetUserDataRequest(), callback);
		}

		// 던전 클리어 — 서버가 보상(exp 등)을 가산 저장 후 반환.
		public void RequestDungeonClear(DungeonClearRequest request, ResponseCallback<DungeonClearResponse> callback)
		{
			if (ensureLoggedIn(callback) == false)
			{
				return;
			}

			_caller.Invoke<DungeonClearRequest, DungeonClearResponse>(FunctionName.DungeonClear, request, callback);
		}

		// 미로그인 상태면 즉시 실패 콜백 — 오프라인/Dev 경로(DevTester 는 Account 직접 설정이라 무관).
		private bool ensureLoggedIn<TResponse>(ResponseCallback<TResponse> callback)
			where TResponse : ServerResponse
		{
			if (IsLoggedIn == false)
			{
				Debug.LogWarning("[NetworkManager] 미로그인 — 함수 호출 무시");
				callback?.Invoke(false, null, "not logged in");
				return false;
			}

			return true;
		}
	}
}
