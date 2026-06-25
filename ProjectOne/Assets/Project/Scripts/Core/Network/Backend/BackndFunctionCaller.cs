using System.Collections.Generic;
using UnityEngine;
using BackEnd;
using ProjectOne.Shared;
using ProjectOne.UI;

namespace ProjectOne.Network
{
	// 뒤끝 함수 호출 담당 — 요청 직렬화 / 비동기 호출(SendQueue) / 응답 역직렬화 / 콜백 디스패치.
	//
	// 규약(단일 진입점 + action 분기):
	//  - 뒤끝 콘솔엔 단일 펑션(FunctionName.Deployed)만 등록한다. 항상 그 이름으로 InvokeFunction 한다.
	//  - body 의 "action" 에 분기 키(GetUserData 등), "req" 에 요청 DTO(JsonUtility) 를 담는다.
	//    → 서버 진입점이 action 으로 실제 핸들러로 분기하고, 핸들러는 "req" 를 파싱한다.
	//  - 응답: 서버가 TResponse(ServerResponse 파생) 형태 JSON 으로 반환 → JsonUtility 역직렬화.
	//
	// 뒤끝 동기 InvokeFunction 은 내부적으로 Unity 메인스레드 API 를 쓰므로 스레드풀로 보낼 수 없다.
	// SendQueue 를 쓰면 네트워크 호출은 워커 스레드에서, 콜백은 SendQueueMgr.Poll() 시점에 메인스레드에서 실행된다.
	public sealed class BackndFunctionCaller
	{
		private const string ActionKey = "action";
		private const string BodyKey = "req";

		// 진행 중 요청(action) 집합 — 응답 전 같은 요청 재전송을 막는다(중복 전송 방지).
		private readonly HashSet<string> _inFlight = new HashSet<string>();

		// 함수 호출 진입점 — 콜백 API. SendQueue 에 적재하고 결과를 콜백으로 돌려준다.
		// showOverlay: 응답 대기 동안 네트워크 딤(UIManager)을 띄울지. 백그라운드 flush 등은 false.
		public void Invoke<TRequest, TResponse>(string action, TRequest request, ResponseCallback<TResponse> callback, bool showOverlay = true)
			where TRequest : class
			where TResponse : ServerResponse
		{
			// 같은 요청이 아직 진행 중이면 무시한다(drop) — 응답 전 중복 전송 차단.
			if (_inFlight.Contains(action) == true)
			{
				Debug.LogWarning($"[NetworkManager] 요청 진행 중 — 중복 호출 무시({action})");
				return;
			}

			_inFlight.Add(action);

			if (showOverlay == true && UIManager.HasInstance == true)
			{
				UIManager.Instance.ShowNetworkBlocker();
			}

			string reqJson = (request != null) ? JsonUtility.ToJson(request) : "{}";

			Param body = new Param();
			body.Add(ActionKey, action);
			body.Add(BodyKey, reqJson);

			// 네트워크는 워커 스레드, 콜백은 SendQueueMgr.Poll() 시 메인스레드 실행.
			// SendQueue 콜백은 Action<BackendReturnObject> 라 제네릭 컨텍스트(action/callback/showOverlay) 전달을 위해
			// 어댑터 람다 1줄만 사용(람다 금지 규칙의 예외 케이스).
			// funcName 은 항상 단일 진입점(ProjectOneFunction). 분기는 body 의 action 으로.
			SendQueue.Enqueue(Backend.BFunc.InvokeFunction, FunctionName.Deployed, body,
				bro => handleResponse(action, bro, callback, showOverlay));
		}

		// 응답 처리 — 성공/실패 분기 후 역직렬화하여 콜백 호출. (메인스레드에서 호출됨)
		private void handleResponse<TResponse>(string action, BackendReturnObject bro, ResponseCallback<TResponse> callback, bool showOverlay)
			where TResponse : ServerResponse
		{
			// 응답 도착 — 콜백 내 재요청(재시도)을 막지 않도록 분기 전에 먼저 진행 상태/딤을 해제한다.
			_inFlight.Remove(action);

			if (showOverlay == true && UIManager.HasInstance == true)
			{
				UIManager.Instance.HideNetworkBlocker();
			}

			if (bro.IsSuccess() == false)
			{
				Debug.LogError($"[NetworkManager] 함수 실패({action}): {bro.GetMessage()}");
				callback?.Invoke(false, null, bro.GetMessage());
				return;
			}

			// 뒤끝 함수 반환은 {"result":"<직렬화된 TResponse>"} 형태로 감싸여 온다. result 를 꺼내 역직렬화한다.
			string respJson = bro.GetReturnValuetoJSON()["result"].ToString();
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
