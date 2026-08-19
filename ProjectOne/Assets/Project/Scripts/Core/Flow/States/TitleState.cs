using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
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

		// 개발용 오프라인 진입 — GameBootstrapper 인스펙터가 설정한다. 기본은 꺼짐(서버 경로 유지).
		//
		// 이게 없으면 로그인이 안 되는 환경에서 2.Title 에 영구 정지한다. 부수 효과로
		// DataLoadedEvent 가 안 떠서 DevTester 도 영원히 발화하지 않는다.
		// 미로그인 진행 자체는 안전하다 — DataLoadState 가 빈 계정으로 통과하도록 이미 되어 있다.
		public static bool SkipLogin { get; set; }

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 뒤끝 초기화 1회 — 로그인 전에 수행.
			NetworkManager.Instance.Init();

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);

			// 로그인 성공 대기 (Button_Guest / Button_Google → NetworkManager.Login)
			if (SkipLogin == true)
			{
				Debug.LogWarning("[TitleState] SkipLogin 이 켜져 있어 로그인 대기를 건너뜁니다 — 빈 계정으로 진행합니다.");
			}
			else
			{
				await UniTask.WaitUntil(isLoggedIn, cancellationToken: ct);
			}

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
