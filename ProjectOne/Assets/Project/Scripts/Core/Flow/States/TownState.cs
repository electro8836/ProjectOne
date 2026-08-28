using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using ProjectOne.Loading;
using ProjectOne.Town;
using ProjectOne.UI;

namespace ProjectOne.Flow
{
	// 마을 상태 — 게임의 허브. 포탈로 필드에, UI로 던전에 들어간다.
	// 마을 귀환은 전체 회복 지점이다 (기반테이블 5.3).
	public class TownState : IGameState
	{
		public const string SceneName = "3.Town";


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

			// 전역 MainHUD — 씬별 HUD 를 두지 않고 이 하나가 씬을 가로질러 산다.
			// **히어로 스폰(TownDirector.Begin)보다 먼저다.** 조이스틱이 UnitSpawnedEvent 로
			// 히어로를 찾으므로 순서가 뒤집히면 이벤트를 놓친다.
			await UIManager.Instance.EnsureMainHudAsync(ct);

			// 창(200)·팝업(300) 위에 상시 뜨는 하단 네비게이션 — 전용 Navigation(350) 캔버스에 붙는다.
			await UIManager.Instance.EnsureNavigationBarAsync(ct);

			// 마을 맵 + NPC 배치. 씬은 비어 있고 코드가 띄운다 (맵 설계 8장).
			TownDirector director = TownDirector.EnsureInstance();
			await director.Begin(ct);

			// 씬 준비 — 마을 오브젝트의 Awake/Start 가 Account 데이터로 구성되도록 1프레임 대기 후 해제.
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
