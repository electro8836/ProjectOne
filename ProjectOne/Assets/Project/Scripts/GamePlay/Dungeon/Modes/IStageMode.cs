using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Dungeon
{
	// 던전 단계의 클리어 방식. 신규 설계에서는 모드 컬럼을 두지 않고
	// DungeonType(Gold/Rift) enum 하나가 곧 규칙 하나다 (맵 설계 9장).
	// 던전마다 단계 테이블이 달라 행 대신 진입 파라미터를 받고, 모드가 자기 테이블을 읽는다.
	public interface IStageMode
	{
		UniTask SetupAsync(DungeonContext ctx, CancellationToken ct);
		DungeonResult CheckResult();
	}
}
