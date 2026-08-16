using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using ProjectOne.Loading;
using ProjectOne.UI;

namespace ProjectOne.Flow
{
	// 마을 상태 — 게임의 허브. 포탈로 필드에, UI로 던전에 들어간다.
	// 마을 귀환은 전체 회복 지점이다 (기반테이블 5.3).
	public class TownState : IGameState
	{
		public const string SceneName = "3.Town";

		// 마을 HUD — 씬에 박지 않고 Addressable 로 띄운다.
		private const string HudAddress = "UI_TownHUD";

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 두 경로 공유: ToTown(상류 PatchState 가 이미 표시 시작) / ReturnTown(필드·던전에서 복귀).
			// 표시 중이 아니면 복귀 흐름이므로 직접 시작한다.
			if (LoadingManager.Instance.IsShowing == false)
			{
				await LoadingManager.Instance.ShowAsync(LoadingFlow.ReturnTown, ct);
			}

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			await SceneHud.LoadAsync(HudAddress, ct);

			// 씬 준비 — 마을 오브젝트의 Awake/Start 가 Account 데이터로 구성되도록 1프레임 대기 후 해제.
			await UniTask.NextFrame(ct);
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 1f);
			await LoadingManager.Instance.HideAsync();
		}

		public UniTask ExitAsync()
		{
			SceneHud.Unload();
			return UniTask.CompletedTask;
		}

		private void onSceneLoadProgress(float ratio)
		{
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, ratio);
		}
	}
}
