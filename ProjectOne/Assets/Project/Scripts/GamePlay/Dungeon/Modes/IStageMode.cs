using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;

namespace ProjectOne.Dungeon
{
	// 스테이지 모드 규칙 — MapModeType 별 진행/승패 판정을 캡슐화한다.
	// 맵 로드와 히어로 스폰은 DungeonDirector 가 던전 단위로 처리하므로, 모드는 몬스터/규칙만 담당한다.
	public interface IStageMode
	{
		// 몬스터 스폰·진행 루프 시작. DungeonDirector 가 스테이지 진입 시 1회 호출.
		UniTask SetupAsync(Table_Stage.Row stage, CancellationToken ct);

		// 매 프레임 스테이지 승패 판정 — DungeonDirector 가 폴링한다.
		DungeonResult CheckResult();
	}
}
