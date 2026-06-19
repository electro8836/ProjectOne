using Cysharp.Threading.Tasks;
using BackEnd;

namespace ProjectOne.Network
{
	// 디바이스(게스트) 로그인 — 기기 고유 ID 기반 계정. 최초 호출 시 계정 자동 생성.
	// 동기 호출이지만 인터페이스 통일을 위해 UniTask 로 감싼다.
	public sealed class GuestLoginHandler : ILoginHandler
	{
		public UniTask<(bool success, string error)> LoginAsync()
		{
			BackendReturnObject bro = Backend.BMember.GuestLogin();
			if (bro.IsSuccess() == false)
			{
				return UniTask.FromResult((false, bro.GetMessage()));
			}

			return UniTask.FromResult((true, (string)null));
		}
	}
}
