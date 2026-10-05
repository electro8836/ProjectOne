using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectOne.Loading;
using ProjectOne.Network;
using ProjectOne.UI;

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

		// 타이틀에 도착하면 팝업으로 띄울 안내 — 연결이 끊겨 돌려보낼 때 채운다(DataLoadState.GoToTitleAsync).
		public static string PendingNotice { get; set; }

		public async UniTask EnterAsync(CancellationToken ct)
		{
			// 뒤끝 초기화 1회 — 로그인 전에 수행.
			NetworkManager.Instance.Init();

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);

			// 게임 중 연결이 끊겨 돌아온 경우 로딩 화면을 띄운 채 넘어온다 — 로그인 버튼이 보이도록 걷는다.
			if (LoadingManager.Instance.IsShowing == true)
			{
				await LoadingManager.Instance.HideAsync();
			}

			await showPendingNoticeAsync(ct);

			// 로그인 결과 대기 (Button_Guest / Button_Google → NetworkManager.Login)
			//
			// 성공이 아니라 **결과**를 기다린다. 서버가 죽어 실패해도 오프라인으로 진행한다 —
			// 성공만 기다리면 서버 장애가 곧 개발 중단이 된다.
			if (SkipLogin == true)
			{
				Debug.LogWarning("[TitleState] SkipLogin 이 켜져 있어 로그인 대기를 건너뜁니다 — 빈 계정으로 진행합니다.");
			}
			else
			{
				await UniTask.WaitUntil(isLoginResolved, cancellationToken: ct);

				if (NetworkManager.Instance.IsLoggedIn == false)
				{
					Debug.LogWarning("[TitleState] 로그인에 실패했습니다 — 빈 계정으로 진행합니다.");
				}
			}

			GameFlow.Instance.ChangeStateAsync(new PatchState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		// 돌려보낸 이유를 알리고 확인을 받는다. 팝업이 떠 있는 동안은 Dim 이 로그인 버튼을 가린다.
		private static async UniTask showPendingNoticeAsync(CancellationToken ct)
		{
			if (string.IsNullOrEmpty(PendingNotice) == true)
			{
				return;
			}

			CommonPopupData data;
			data.title = NetworkMessages.Title;
			data.desc = PendingNotice;
			data.button1Text = NetworkMessages.Confirm;
			data.button2Text = string.Empty;
			PendingNotice = null;

			await UIManager.Instance.ShowCommonPopupAsync(data, ct);
		}

		private bool isLoginResolved()
		{
			return NetworkManager.Instance.IsLoggedIn || NetworkManager.Instance.LoginAttempted;
		}
	}
}
