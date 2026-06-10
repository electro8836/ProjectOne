using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ProjectOne.Flow
{
	public class LobbyState : IGameState
	{
		private const string SceneName = "3.Lobby";

		public async UniTask EnterAsync(CancellationToken ct)
		{
			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
