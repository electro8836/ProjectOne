using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Flow
{
	// 패치 상태 — Title 씬 위 오버레이(별도 씬 없음).
	// TODO: Addressables 카탈로그 갱신 + 번들 다운로드 (백엔드 준비 후 EnterAsync 안만 채우면 됨)
	public class PatchState : IGameState
	{
		public UniTask EnterAsync(CancellationToken ct)
		{
			// 스텁 — 즉시 다음 상태로 전이
			GameFlow.Instance.ChangeStateAsync(new LoginState()).Forget();
			return UniTask.CompletedTask;
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
