using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using ProjectOne.Loading;

namespace ProjectOne.Flow
{
	public class LobbyState : IGameState
	{
		private const string SceneName = "3.Lobby";

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 두 경로 공유: ToLobby(상류 PatchState 가 이미 표시 시작) / ReturnToLobby(배틀→로비 복귀, 표시 안 됨).
			// 표시 중이 아니면 복귀 흐름이므로 직접 시작한다.
			if (LoadingManager.Instance.IsShowing == false)
			{
				await LoadingManager.Instance.ShowAsync(LoadingFlow.ReturnToLobby, ct);
			}

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			// 씬 준비 — 로비 오브젝트의 Awake/Start 가 Account 데이터로 구성되도록 1프레임 대기 후 해제.
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			await UniTask.NextFrame(ct);
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
