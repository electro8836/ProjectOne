using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Battle
{
	// 전투 모드 규칙 — 맵/유닛 셋업과 승패 판정을 모드별로 캡슐화한다.
	public interface IBattleMode
	{
		// 맵 로드 + 파티/몬스터 스폰 구성. BattleDirector 가 진입 시 1회 호출.
		UniTask SetupAsync(BattleContext ctx, BattleDirector dir, CancellationToken ct);

		// 매 프레임 승패 판정 — BattleDirector 가 폴링한다.
		BattleResult CheckResult();
	}
}
