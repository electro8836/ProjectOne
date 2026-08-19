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

			if (_current != null)
			{
				bool exitCancelled = await _current.ExitAsync().SuppressCancellationThrow();
				if (exitCancelled) { return; }
			}

			_current = next;

			bool entered = await enterGuardedAsync(next, ct);
			if (entered == false) { return; }

			EventManager.Instance.Publish(new GameStateChangedEvent(next.GetType()));
		}

		// 전이 예외를 여기서 잡는다 — 프로젝트 규칙(try-catch 금지)에서 의도적으로 벗어난 유일한 지점이다.
		//
		// 각 상태는 EnterAsync 를 .Forget() 체인으로 호출하므로 예외가 관측되지 않고,
		// 증상이 "아무 메시지 없이 로딩 화면 고착"이라 원인을 추적할 수 없다.
		// SuppressCancellationThrow() 는 OperationCanceledException 만 삼키므로 나머지를 덮지 못한다.
		//
		// 삼키지 않는다 — 예외 전문을 남기고 로딩만 걷어 다음 조작이 가능한 상태로 되돌린다.
		private static async UniTask<bool> enterGuardedAsync(IGameState next, CancellationToken ct)
		{
			try
			{
				bool cancelled = await next.EnterAsync(ct).SuppressCancellationThrow();
				return cancelled == false;
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[GameFlow] 상태 진입 실패 — {next.GetType().Name}");
				Debug.LogException(e);

				await hideLoadingAsync();
				return false;
			}
		}

		// 로딩이 떠 있으면 걷는다. 이게 없으면 예외 이후 화면이 영구 고착된다.
		private static async UniTask hideLoadingAsync()
		{
			if (Loading.LoadingManager.HasInstance == false)
			{
				return;
			}

			if (Loading.LoadingManager.Instance.IsShowing == false)
			{
				return;
			}

			await Loading.LoadingManager.Instance.HideAsync().SuppressCancellationThrow();
		}
	}
}
