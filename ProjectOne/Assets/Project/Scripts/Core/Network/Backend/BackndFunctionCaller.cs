using Cysharp.Threading.Tasks;
using UnityEngine;
using BackEnd;
using ProjectOne.Shared;

namespace ProjectOne.Network
{
	// 뒤끝 함수 호출 담당 — 요청 직렬화 / 백그라운드 호출 / 메인스레드 복귀 / 응답 역직렬화 / 콜백 디스패치.
	//
	// 규약:
	//  - 요청: 요청 DTO 를 JsonUtility 로 직렬화해 body 의 "req" 에 담는다 → 서버 함수가 event.param.req 를 파싱.
	//  - 응답: 서버가 TResponse(ServerResponse 파생) 형태 JSON 으로 반환 → JsonUtility 역직렬화.
	//
	// InvokeFunction 동기 호출은 네트워크라 메인스레드를 블로킹하므로 스레드풀에서 실행 후 메인스레드로 복귀한다.
	public sealed class BackndFunctionCaller
	{
		private const string BodyKey = "req";

		// 함수 호출 진입점 — 콜백 API. 내부에서 비동기로 처리하고 결과를 콜백으로 돌려준다.
		public void Invoke<TRequest, TResponse>(string action, TRequest request, ResponseCallback<TResponse> callback)
			where TRequest : class
			where TResponse : ServerResponse
		{
			invokeAsync(action, request, callback).Forget();
		}

		private async UniTaskVoid invokeAsync<TRequest, TResponse>(string action, TRequest request, ResponseCallback<TResponse> callback)
			where TRequest : class
			where TResponse : ServerResponse
		{
			string reqJson = (request != null) ? JsonUtility.ToJson(request) : "{}";

			Param body = new Param();
			body.Add(BodyKey, reqJson);

			// 네트워크 블로킹 호출을 스레드풀로 보내 메인스레드 정지를 막는다.
			BackendReturnObject bro = await UniTask.RunOnThreadPool(() => Backend.BFunc.InvokeFunction(action, body));
			await UniTask.SwitchToMainThread();

			if (bro.IsSuccess() == false)
			{
				Debug.LogError($"[NetworkManager] 함수 실패({action}): {bro.GetMessage()}");
				callback?.Invoke(false, null, bro.GetMessage());
				return;
			}

			string respJson = bro.GetReturnValuetoJSON().ToJson();
			TResponse resp = JsonUtility.FromJson<TResponse>(respJson);
			if (resp == null)
			{
				callback?.Invoke(false, null, "응답 역직렬화 실패");
				return;
			}

			callback?.Invoke(resp.success, resp, resp.error);
		}
	}
}
