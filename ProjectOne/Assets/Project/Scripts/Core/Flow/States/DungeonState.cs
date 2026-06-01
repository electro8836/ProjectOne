using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ProjectOne.Flow
{
	// 던전 상태 — 5.Dungeon 씬 로드. Stage ⇅ Dungeon 입퇴장.
	public class DungeonState : IGameState
	{
		private const string SceneName = "5.Dungeon";

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
