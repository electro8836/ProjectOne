using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Flow
{
	// 로그인 상태 — 씬 로드 없이 서버 인증만 수행하는 논리 단계.
	// 씬 로드는 다음 LobbyState 한 곳에서만 한다.
	public class LoginState : IGameState
	{
		public async UniTask EnterAsync(CancellationToken ct)
		{
			// TODO: 서버 인증/로그인 (백엔드 준비 후 구현)
			await UniTask.CompletedTask;

			GameFlow.Instance.ChangeStateAsync(new DataLoadState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
