using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ProjectOne.Flow
{
	public class BattleState : IGameState
	{
		private const string SceneName = "4.Battle";

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
