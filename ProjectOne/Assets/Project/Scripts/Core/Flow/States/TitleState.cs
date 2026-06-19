using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using ProjectOne.Network;

namespace ProjectOne.Flow
{
	// 타이틀 상태 — 2.Title 씬 로드 후 로그인 대기.
	// 로그인(게스트/구글)은 씬의 TitleHUD 가 버튼으로 수행하고,
	// 여기서는 NetworkManager.IsLoggedIn 이 true 가 되면 다음 상태로 전이한다.
	public class TitleState : IGameState
	{
		private const string SceneName = "2.Title";

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 뒤끝 초기화 1회 — 로그인 전에 수행.
			NetworkManager.Instance.Init();

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);

			// 로그인 성공 대기 (Button_Guest / Button_Google → NetworkManager.Login)
			await UniTask.WaitUntil(isLoggedIn, cancellationToken: ct);

			GameFlow.Instance.ChangeStateAsync(new PatchState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		private bool isLoggedIn()
		{
			return NetworkManager.Instance.IsLoggedIn;
		}
	}
}
