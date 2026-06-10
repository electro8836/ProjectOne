using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Flow
{
	// 유저 데이터 로드 상태 — Login 씬 위 오버레이(별도 씬 없음).
	// TODO: 유저 데이터 로드 (로그인 성공 후, 백엔드 준비 후 구현)
	public class DataLoadState : IGameState
	{
		public UniTask EnterAsync(CancellationToken ct)
		{
			// 스텁 — 즉시 다음 상태로 전이
			GameFlow.Instance.ChangeStateAsync(new LobbyState()).Forget();
			return UniTask.CompletedTask;
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
