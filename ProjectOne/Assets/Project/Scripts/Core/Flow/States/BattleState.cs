using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectOne.Battle;
using ProjectOne.Loading;

namespace ProjectOne.Flow
{
	public class BattleState : IGameState
	{
		private const string SceneName = "4.Battle";

		private readonly BattleContext _context;

		public BattleState(BattleContext context)
		{
			_context = context;
		}

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 로딩 화면 표시 (ToBattle 흐름).
			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToBattle, ct);

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			// 씬 로드 경계를 넘어 컨텍스트를 BattleDirector 에 주입
			BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
			if (director == null)
			{
				Debug.LogError("[BattleState] 씬에 BattleDirector 없음");
				await LoadingManager.Instance.HideAsync();
				return;
			}

			// 전투 셋업 완료(스폰·플로우필드 등)까지 await 한 뒤 로딩 해제 — "준비 완료" 후 내린다.
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
