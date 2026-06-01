using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Event;

namespace ProjectOne.Flow
{
	// 게임 진행을 상태(State) 전이로만 표현하는 순수 C# 싱글톤.
	// - 상태마다 진입/이탈 시 할 일을 IGameState로 캡슐화
	// - Update 없음 — 흐름의 책임은 "전이"지 매 프레임 로직이 아님
	// - 전이 도중 새 전이가 들어오면 이전 전이를 CancellationToken으로 취소(직렬화)
	public class GameFlow : Singleton<GameFlow>
	{
		private IGameState _current;
		private CancellationTokenSource _transitionCts;

		// 현재 활성 상태(전이 완료 전에는 진입 중인 상태를 가리킬 수 있음)
		public IGameState CurrentState => _current;

		protected GameFlow() { }

		// 다음 상태로 전이.
		// 각 상태가 EnterAsync 말미에서 이 메서드를 .Forget()으로 호출해 연쇄한다.
		// 내부 try/catch로 미관측 예외를 흡수한다(.Forget() 종료 크래시 방지).
		public async UniTask ChangeStateAsync(IGameState next)
		{
			if (next == null)
			{
				return;
			}

			// 진행 중이던 전이가 있으면 취소하고 새 토큰 발급
			if (_transitionCts != null)
			{
				_transitionCts.Cancel();
				_transitionCts.Dispose();
			}
			_transitionCts = new CancellationTokenSource();
			CancellationToken ct = _transitionCts.Token;

			try
			{
				if (_current != null)
				{
					await _current.ExitAsync();
				}

				_current = next;
				await next.EnterAsync(ct);

				EventManager.Instance.Publish(new GameStateChangedEvent(next.GetType()));
			}
			catch (OperationCanceledException)
			{
				// 다른 전이에 의해 취소됨 — 정상 흐름
			}
			catch (Exception e)
			{
				Debug.LogError("상태 전이 실패: " + next.GetType().Name + " / " + e.Message);
			}
		}
	}
}
