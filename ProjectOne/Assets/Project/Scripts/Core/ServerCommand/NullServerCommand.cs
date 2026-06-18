using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.ServerData
{
	// IServerCommand 기본(로그인 전) 스텁 — 서버 미연결 상태.
	// 로그인 성공 시 BackndServerCommand 로 교체된다. 서버 없이 실행할 땐 DevTester 가 Account 를 직접 설정한다.
	public sealed class NullServerCommand : IServerCommand
	{
		public UniTask<TResponse> ExecuteAsync<TRequest, TResponse>(string action, TRequest request, CancellationToken ct)
			where TRequest : class
			where TResponse : class
		{
			Debug.LogWarning($"[ServerCommand] 서버 미연결 — 액션 무시: {action}");
			return UniTask.FromResult<TResponse>(null);
		}
	}
}
