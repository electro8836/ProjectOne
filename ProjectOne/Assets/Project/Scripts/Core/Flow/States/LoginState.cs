using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ProjectOne.Flow
{
	// 로그인 상태 — 3.Lobby 씬 로드 후 서버 인증.
	public class LoginState : IGameState
	{
		private const string SceneName = "3.Lobby";

		public async UniTask EnterAsync(CancellationToken ct)
		{
			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);

			// TODO: 서버 인증/로그인 (백엔드 준비 후 구현)

			GameFlow.Instance.ChangeStateAsync(new DataLoadState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
