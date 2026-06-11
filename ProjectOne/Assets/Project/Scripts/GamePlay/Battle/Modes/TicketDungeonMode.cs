using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.Battle
{
	// 티켓 던전 모드 — 자리만 확보(stub). 입장권 소비/제한 입장 규칙은 추후 구현.
	public sealed class TicketDungeonMode : IBattleMode
	{
		public UniTask SetupAsync(BattleContext ctx, BattleDirector dir, CancellationToken ct)
		{
			Debug.LogWarning("[TicketDungeonMode] 미구현 — 일반 던전 모드로 대체 권장");
			return UniTask.CompletedTask;
		}

		public BattleResult CheckResult()
		{
			return BattleResult.InProgress;
		}
	}
}
