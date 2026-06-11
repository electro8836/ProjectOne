using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectOne.Battle;

namespace ProjectOne.Flow
{
	public class BattleState : IGameState
	{
		private const string SceneName = "4.Battle";

		private readonly BattleContext _context;

		public BattleState(BattleContext context)
		{
			_context = context;
		}

		public async UniTask EnterAsync(CancellationToken ct)
		{
			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(cancellationToken: ct);

			// 씬 로드 경계를 넘어 컨텍스트를 BattleDirector 에 주입
			BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
			if (director == null)
			{
				Debug.LogError("[BattleState] 씬에 BattleDirector 없음");
				return;
			}

			director.Begin(_context);
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}
	}
}
