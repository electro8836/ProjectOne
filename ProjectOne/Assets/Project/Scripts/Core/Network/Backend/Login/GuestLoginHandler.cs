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
				// 로컬에 캐시된 게스트 자격증명이 서버 계정과 어긋난 경우(예: 서버에서 계정 삭제 → bad customId).
				// 로컬 게스트 정보를 비우고 새 게스트 계정으로 1회 재시도한다.
				Backend.BMember.DeleteGuestInfo();
				bro = Backend.BMember.GuestLogin();
				if (bro.IsSuccess() == false)
				{
					return UniTask.FromResult((false, bro.GetMessage()));
				}
			}

			return UniTask.FromResult((true, (string)null));
		}
	}
}
