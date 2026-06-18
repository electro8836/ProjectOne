using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using BackEnd;

namespace ProjectOne.ServerData
{
	// IServerCommand 의 Backnd 구현 — 액션(= 뒤끝 함수명)을 Backend.BFunc.InvokeFunction 으로 호출한다.
	// 결과를 "서버가 계산해 반환"(서버 권위). 보안: 뽑기/보상 결과를 클라가 만들지 않고 서버가 결정한다.
	//
	// 규약:
	//  - 요청: 요청 DTO 를 JsonUtility 로 직렬화해 body 의 "req" 에 담는다 → 서버 함수가 event.param.req 를 파싱.
	//  - 응답: 서버가 TResponse(ServerResponse 파생: success/error/sync/grants) 형태 JSON 으로 반환 → JsonUtility 역직렬화.
	//  - 호출부는 결과의 sync 를 Account.ApplySync 로 반영하고 grants 를 연출한다.
	//
	// 주의: InvokeFunction 동기 호출은 네트워크라 메인스레드 블로킹(이산 액션이라 허용). 비동기화는 추후 SendQueue.
	public sealed class BackndServerCommand : IServerCommand
	{
		private const string BodyKey = "req";

		public UniTask<TResponse> ExecuteAsync<TRequest, TResponse>(string action, TRequest request, CancellationToken ct)
			where TRequest : class
			where TResponse : class
		{
			string reqJson = (request != null) ? JsonUtility.ToJson(request) : "{}";

			Param body = new Param();
			body.Add(BodyKey, reqJson);

			BackendReturnObject bro = Backend.BFunc.InvokeFunction(action, body);
			if (bro.IsSuccess() == false)
			{
				Debug.LogError($"[BackndServerCommand] 함수 실패({action}): {bro.GetMessage()}");
				return UniTask.FromResult<TResponse>(null);
			}

			string respJson = bro.GetReturnValuetoJSON().ToJson();
			TResponse resp = JsonUtility.FromJson<TResponse>(respJson);
			return UniTask.FromResult(resp);
		}
	}
}
