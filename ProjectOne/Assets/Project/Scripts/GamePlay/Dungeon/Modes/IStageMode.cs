using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;

namespace ProjectOne.Dungeon
{
	// 던전 단계의 클리어 방식. 신규 설계에서는 모드 컬럼을 두지 않고
	// DungeonType(Gold/Exp) enum 하나가 곧 규칙 하나다 (맵 설계 9장).
	public interface IStageMode
	{
		UniTask SetupAsync(Table_DungeonStage.Row stage, CancellationToken ct);
		DungeonResult CheckResult();
	}
}
