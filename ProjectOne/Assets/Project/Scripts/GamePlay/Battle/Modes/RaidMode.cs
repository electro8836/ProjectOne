using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.Battle
{
	// 레이드 모드 — 자리만 확보(stub). 보스 페이즈/제한시간/협동 규칙은 추후 구현.
	public sealed class RaidMode : IBattleMode
	{
		public UniTask SetupAsync(BattleContext ctx, BattleDirector dir, CancellationToken ct)
		{
			Debug.LogWarning("[RaidMode] 미구현 — 일반 던전 모드로 대체 권장");
			return UniTask.CompletedTask;
		}

		public BattleResult CheckResult()
		{
			return BattleResult.InProgress;
		}
	}
}
