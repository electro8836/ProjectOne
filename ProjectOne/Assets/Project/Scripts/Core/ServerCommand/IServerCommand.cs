using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.ServerData
{
	// 액션 계층(server-authoritative) 추상화 — 뽑기/보상/로드처럼 결과를 서버가 계산/반환해야 하는 명령.
	// - action 은 Backnd 함수명, request/response 는 JSON 직렬화 대상 DTO.
	// - 구현: 로그인 전 NullServerCommand(스텁), 로그인 후 BackndServerCommand(뒤끝 함수 호출).
	public interface IServerCommand
	{
		UniTask<TResponse> ExecuteAsync<TRequest, TResponse>(string action, TRequest request, CancellationToken ct)
			where TRequest : class
			where TResponse : class;
	}
}
