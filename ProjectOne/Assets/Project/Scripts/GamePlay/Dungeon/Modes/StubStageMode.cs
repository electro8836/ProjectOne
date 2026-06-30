using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;

namespace ProjectOne.Dungeon
{
	// 미구현 모드(BuildingDestroy/Capture/Breakthrough) 임시 스텁 — 진입 즉시 클리어 처리한다.
	// 해당 기믹 오브젝트가 준비되면 정식 모드로 교체한다.
	internal sealed class StubStageMode : IStageMode
	{
		public UniTask SetupAsync(Table_Stage.Row stage, CancellationToken ct)
		{
			// 미구현 모드는 진입 즉시 클리어 — 히어로 위치에 상점 기믹 등장 (다음 스테이지 진입 시 회수)
			GimmickService.Instance.SpawnAtHeroAsync(GimmickService.ShopGimmickAddress).Forget();

			return UniTask.CompletedTask;
		}

		public DungeonResult CheckResult()
		{
			return DungeonResult.Cleared;
		}
	}
}
