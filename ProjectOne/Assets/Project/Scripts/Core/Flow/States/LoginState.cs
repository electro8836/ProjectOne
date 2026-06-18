using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Flow
{
	// 로그인 상태 — 씬 로드 없이 진행하는 논리 단계.
	// 실제 로그인은 타이틀 화면(TitleLoginController)에서 수행되므로 여기선 데이터 로드로 넘어간다.
	public class LoginState : IGameState
	{
		public async UniTask EnterAsync(CancellationToken ct)
		{
			await UniTask.CompletedTask;

			GameFlow.Instance.ChangeStateAsync(new DataLoadState()).Forget();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
