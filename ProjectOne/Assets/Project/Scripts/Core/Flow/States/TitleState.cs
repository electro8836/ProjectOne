using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using ProjectOne.ServerData;

namespace ProjectOne.Flow
{
	// 타이틀 상태 — 2.Title 씬 로드 후 로그인 대기.
	// 로그인(게스트/구글)은 씬의 TitleLoginController 가 버튼으로 수행하고,
	// 여기서는 BackndInitializer.IsLoggedIn 이 true 가 되면 다음 상태로 전이한다.
	public class TitleState : IGameState
	{
		private const string SceneName = "2.Title";

		public async UniTask EnterAsync(CancellationToken ct)
		{
			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);

			// 로그인 성공 대기 (Button_Guest / Button_Google → BackndInitializer)
			await UniTask.WaitUntil(isLoggedIn, cancellationToken: ct);

			GameFlow.Instance.ChangeStateAsync(new PatchState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		private bool isLoggedIn()
		{
			return BackndInitializer.IsLoggedIn;
		}
	}
}
