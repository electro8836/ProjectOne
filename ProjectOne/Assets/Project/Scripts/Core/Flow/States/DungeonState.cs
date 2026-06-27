using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectOne.Dungeon;
using ProjectOne.Loading;

namespace ProjectOne.Flow
{
	// 던전 진입 상태 — 전투 씬을 로드하고 씬의 DungeonDirector 에 컨텍스트를 주입한다.
	public class DungeonState : IGameState
	{
		private const string SceneName = "4.Battle";

		private readonly DungeonContext _context;

		public DungeonState(DungeonContext context)
		{
			_context = context;
		}

		public async UniTask EnterAsync(CancellationToken ct)
		{
			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToBattle, ct);

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			// 씬 로드 경계를 넘어 컨텍스트를 DungeonDirector 에 주입
			DungeonDirector director = Object.FindAnyObjectByType<DungeonDirector>();
			if (director == null)
			{
				Debug.LogError("[DungeonState] 씬에 DungeonDirector 없음");
				await LoadingManager.Instance.HideAsync();
				return;
			}

			// 히어로 스폰·첫 스테이지 맵 로드까지 await 한 뒤 로딩 해제 — "준비 완료" 후 내린다.
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
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
