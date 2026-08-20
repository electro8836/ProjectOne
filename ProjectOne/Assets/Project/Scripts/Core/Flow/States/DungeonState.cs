using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectOne.Dungeon;
using ProjectOne.Loading;
using ProjectOne.UI;

namespace ProjectOne.Flow
{
	// 던전 진입 상태 — 던전 씬을 로드하고 DungeonDirector 에 컨텍스트를 주입한다.
	// 씬은 비어 있고 Director·HUD·그리드맵을 전부 코드가 띄운다.
	public class DungeonState : IGameState
	{
		public const string SceneName = "5.Dungeon";


		private readonly DungeonContext _context;

		public DungeonState(DungeonContext context)
		{
			_context = context;
		}

		public async UniTask EnterAsync(CancellationToken ct)
		{
			if (LoadingManager.Instance.IsShowing == false)
			{
				await LoadingManager.Instance.ShowAsync(LoadingFlow.ToDungeon, ct);
			}

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			// 히어로 스폰·맵 로드까지 await 한 뒤 로딩을 내린다 — "준비 완료" 후에 걷는다.
			DungeonDirector director = DungeonDirector.EnsureInstance();
			await director.Begin(_context);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 1f);
			await LoadingManager.Instance.HideAsync();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		private void onSceneLoadProgress(float ratio)
		{
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, ratio);
		}
	}
}
