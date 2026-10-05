using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
	// 통신 실패(응답 없음·시간 초과·형식 오류):
	//  - 딤을 띄우는 요청(유저가 직접 누른 요청)은 같은 요청을 스스로 다시 보낸다. 요청마다 번호(rid)를 붙여
	//    서버가 같은 번호를 두 번 반영하지 않게 한다(서버 RequestOps). 콜백은 최종 결과로 한 번만 부른다.
	//  - 딤 없는 요청(백그라운드 저장)은 바로 실패 콜백을 부른다 — 각자 재전송·되돌림을 한다.
	//
	// 뒤끝 동기 InvokeFunction 은 내부적으로 Unity 메인스레드 API 를 쓰므로 스레드풀로 보낼 수 없다.
	// SendQueue 를 쓰면 네트워크 호출은 워커 스레드에서, 콜백은 SendQueueMgr.Poll() 시점에 메인스레드에서 실행된다.
	public sealed class BackndFunctionCaller
	{
		private const string ActionKey = "action";
		private const string BodyKey = "req";
		private const string RidKey = "rid";		// 요청 번호 — 서버 RequestOps.RidKey 와 같아야 한다
		private const string RetryKey = "retry";	// 재전송 표시 — 서버 RequestOps.RetryKey 와 같아야 한다

		// 이 시간 안에 응답이 없으면 통신 실패로 친다 — 딤과 중복 차단이 영영 남지 않게 한다.
		private const float TimeoutSeconds = 20f;
		private const string TimeoutError = "timeout";

		// 통신 실패 시 다시 보내는 횟수와 간격.
		private const int MaxRetries = 3;
		private const float RetryDelaySeconds = 2f;

		// 재전송 대기 중 — 어떤 시도의 응답도 기다리지 않는 상태.
		private const int NoAttempt = 0;

		// 진행 중 요청(action → 지금 기다리는 시도 번호) — 응답 전 같은 요청 재전송을 막는다(중복 전송 방지).
		// 시도 번호는 시간 초과로 넘긴 시도의 뒤늦은 응답을 가려내는 데 쓴다.
		private readonly Dictionary<string, int> _inFlight = new Dictionary<string, int>();
		private int _lastAttemptId;

		// 호출할 펑션 이름 — 항상 단일 진입점이다.
		private string _functionName = FunctionName.Deployed;

		// 요청 1건의 진행 상태. 재전송이 같은 내용으로 다시 나가도록 들고 있는다.
		private sealed class Pending<TResponse> where TResponse : ServerResponse
		{
			public string action;
			public string reqJson;
			public string rid;			// 딤 요청만 — 없으면 null
			public ResponseCallback<TResponse> callback;
			public bool showOverlay;
			public bool notifyRejected;
			public int sendCount;		// 지금까지 보낸 횟수(첫 전송 포함)
			public bool retrying;		// 재연결 표시를 켜 둔 상태인가
		}

		// 함수 호출 진입점 — 콜백 API. SendQueue 에 적재하고 결과를 콜백으로 돌려준다.
		// showOverlay: 응답 대기 동안 네트워크 딤(UIManager)을 띄울지. 백그라운드 flush 등은 false.
		//              딤을 띄우는 요청(유저가 직접 누른 요청)만 통신 실패 시 다시 보내고 실패를 화면에 안내한다.
		// notifyRejected: 서버 거절 안내를 공통 처리에 맡길지. 호출부가 직접 안내하면 false.
		public void Invoke<TRequest, TResponse>(string action, TRequest request, ResponseCallback<TResponse> callback, bool showOverlay = true, bool notifyRejected = true)
			where TRequest : class
			where TResponse : ServerResponse
		{
			// 같은 요청이 아직 진행 중이면 무시한다(drop) — 응답 전 중복 전송 차단.
			if (_inFlight.ContainsKey(action) == true)
			{
				Debug.LogWarning($"[NetworkManager] 요청 진행 중 — 중복 호출 무시({action})");
				return;
			}

			Pending<TResponse> pending = new Pending<TResponse>();
			pending.action = action;
			pending.reqJson = (request != null) ? JsonUtility.ToJson(request) : "{}";
			pending.rid = (showOverlay == true) ? Guid.NewGuid().ToString("N") : null;
			pending.callback = callback;
			pending.showOverlay = showOverlay;
			pending.notifyRejected = notifyRejected;

			_inFlight.Add(action, NoAttempt);

			if (showOverlay == true && UIManager.HasInstance == true)
			{
				UIManager.Instance.ShowNetworkBlocker();
			}

			send(pending);
		}

		// 요청을 한 번 보낸다. 재전송도 같은 번호(rid)로 여기를 다시 탄다.
		private void send<TResponse>(Pending<TResponse> pending) where TResponse : ServerResponse
		{
			pending.sendCount++;

			int attemptId = ++_lastAttemptId;
			_inFlight[pending.action] = attemptId;

			Param body = new Param();
			body.Add(ActionKey, pending.action);
			body.Add(BodyKey, pending.reqJson);
			if (pending.rid != null)
			{
				body.Add(RidKey, pending.rid);
				if (pending.sendCount > 1)
				{
					body.Add(RetryKey, 1);
				}
			}

			// 네트워크는 워커 스레드, 콜백은 SendQueueMgr.Poll() 시 메인스레드 실행.
			// SendQueue 콜백은 Action<BackendReturnObject> 라 제네릭 컨텍스트 전달을 위해
			// 어댑터 람다 1줄만 사용(람다 금지 규칙의 예외 케이스).
			// funcName 은 항상 단일 진입점(ProjectOneFunction). 분기는 body 의 action 으로.
			SendQueue.Enqueue(Backend.BFunc.InvokeFunction, _functionName, body,
				bro => handleResponse(pending, attemptId, bro));

			watchTimeoutAsync(pending, attemptId).Forget();
		}

		// 응답 처리 — 성공/실패 분기 후 역직렬화하여 콜백 호출. (메인스레드에서 호출됨)
		private void handleResponse<TResponse>(Pending<TResponse> pending, int attemptId, BackendReturnObject bro)
			where TResponse : ServerResponse
		{
			// 시간 초과로 이미 넘긴 시도의 뒤늦은 응답 — 다음 시도가 같은 번호(rid)로 결과를 받는다.
			if (isWaiting(pending.action, attemptId) == false)
			{
				Debug.LogWarning($"[NetworkManager] 시간 초과 뒤에 도착한 응답 무시({pending.action})");
				return;
			}

			if (bro.IsSuccess() == false)
			{
				Debug.LogError($"[NetworkManager] 함수 실패({pending.action}): {bro.GetMessage()}");
				onTransportFailed(pending, null, bro.GetMessage());
				return;
			}

			// 뒤끝 함수 반환은 {"result":"<직렬화된 TResponse>"} 형태로 감싸여 온다. result 를 꺼내 역직렬화한다.
			string respJson = bro.GetReturnValuetoJSON()["result"].ToString();
			TResponse resp = JsonUtility.FromJson<TResponse>(respJson);
			if (resp == null)
			{
				onTransportFailed(pending, null, "응답 역직렬화 실패");
				return;
			}

			// 실패인데 사유가 비어 있으면 핸들러가 만든 본문이 아니다(펑션 시간 초과 등 런타임 오류 봉투).
			// 원문을 그대로 남겨 원인을 볼 수 있게 한다. 서버가 처리했는지 알 수 없으므로 통신 실패로 다룬다.
			if (resp.success == false && string.IsNullOrEmpty(resp.error) == true)
			{
				Debug.LogError($"[NetworkManager] 함수 응답 형식 오류({pending.action}): {respJson}");
				onTransportFailed(pending, resp, respJson);
				return;
			}

			complete(pending, resp.success, resp, resp.error);

			// 서버가 사유를 달아 거절했다 — 서버 상태는 바뀌지 않았으므로 안내만 한다.
			if (resp.success == false && pending.showOverlay == true && pending.notifyRejected == true)
			{
				NetworkFailureHandler.OnRejected(pending.action, resp.error);
			}
		}

		// 응답이 끝내 오지 않는 시도를 통신 실패로 넘긴다. 실제 시간으로 센다(일시정지 중에도 흐른다).
		private async UniTaskVoid watchTimeoutAsync<TResponse>(Pending<TResponse> pending, int attemptId)
			where TResponse : ServerResponse
		{
			await UniTask.Delay(TimeSpan.FromSeconds(TimeoutSeconds), DelayType.Realtime);

			if (isWaiting(pending.action, attemptId) == false)
			{
				return;	// 그 사이 응답이 와서 처리됐다
			}

			Debug.LogError($"[NetworkManager] 함수 응답 시간 초과({pending.action})");
			onTransportFailed(pending, null, TimeoutError);
		}

		// 통신 실패 — 딤 요청은 같은 요청을 다시 보내고, 다 써 버리면 실패로 끝낸 뒤 타이틀로 돌려보낸다.
		private void onTransportFailed<TResponse>(Pending<TResponse> pending, TResponse data, string error)
			where TResponse : ServerResponse
		{
			if (pending.showOverlay == false)
			{
				complete(pending, false, data, error);
				return;
			}

			// 첫 전송 1회 + 재전송 MaxRetries 회.
			if (pending.sendCount > MaxRetries)
			{
				complete(pending, false, data, error);
				NetworkFailureHandler.OnRetryExhausted();
				return;
			}

			// 다음 전송까지 어떤 응답도 기다리지 않는다(이 시도의 뒤늦은 응답은 버린다).
			_inFlight[pending.action] = NoAttempt;

			if (pending.retrying == false)
			{
				pending.retrying = true;
				NetworkFailureHandler.BeginRetry();
			}

			Debug.LogWarning($"[NetworkManager] 다시 보낸다 {pending.sendCount}/{MaxRetries}({pending.action}): {error}");
			resendAfterDelayAsync(pending).Forget();
		}

		private async UniTaskVoid resendAfterDelayAsync<TResponse>(Pending<TResponse> pending) where TResponse : ServerResponse
		{
			await UniTask.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), DelayType.Realtime);
			send(pending);
		}

		// 요청을 끝낸다 — 진행 중 목록에서 내리고 딤·재연결 표시를 걷은 뒤 콜백을 한 번 부른다.
		// 콜백 내 재요청(재시도)을 막지 않도록 정리를 콜백보다 먼저 한다.
		private void complete<TResponse>(Pending<TResponse> pending, bool success, TResponse data, string error)
			where TResponse : ServerResponse
		{
			_inFlight.Remove(pending.action);

			if (pending.showOverlay == true && UIManager.HasInstance == true)
			{
				UIManager.Instance.HideNetworkBlocker();
			}

			if (pending.retrying == true)
			{
				pending.retrying = false;
				NetworkFailureHandler.EndRetry();
			}

			pending.callback?.Invoke(success, data, error);
		}

		// 이 시도의 응답을 아직 기다리는 중인가.
		private bool isWaiting(string action, int attemptId)
		{
			int current;
			return _inFlight.TryGetValue(action, out current) == true && current == attemptId;
		}
	}
}
