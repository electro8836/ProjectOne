using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ProjectOne.Flow
{
	// 스테이지 상태 — 4.Battle 씬 로드. 메인 전투/진행.
	// (전투 로직은 이번 범위 밖 — 씬 로드 + 전이 골격만)
	public class StageState : IGameState
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
