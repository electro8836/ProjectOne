using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace ProjectOne.Flow
{
	// 타이틀 상태 — 2.Title 씬 로드 후 "터치하여 시작" 입력 대기.
	public class TitleState : IGameState
	{
		private const string SceneName = "2.Title";

		public async UniTask EnterAsync(CancellationToken ct)
		{
			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);

			// 터치/클릭 대기
			await UniTask.WaitUntil(isStartTriggered, cancellationToken: ct);

			GameFlow.Instance.ChangeStateAsync(new PatchState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		// 마우스/터치 등 포인터 입력 감지 (새 Input System)
		private bool isStartTriggered()
		{
			Pointer pointer = Pointer.current;
			if (pointer == null)
			{
				return false;
			}

			return pointer.press.wasPressedThisFrame;
		}
	}
}
