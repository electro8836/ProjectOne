using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Flow
{
	// 게임 흐름의 한 상태.
	// - EnterAsync: 진입 시 할 일(씬 로드, 시스템 on, 다음 상태 전이 등). ct는 GameFlow가 발급한 전이 토큰.
	// - ExitAsync: 이탈 시 정리. 다음 상태 진입 직전에 호출된다.
	public interface IGameState
	{
		UniTask EnterAsync(CancellationToken ct);

		UniTask ExitAsync();
	}
}
