using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 스테이지 모드 공통부 — 진행 루프(RunAsync)를 취소 안전하게 구동하고, 히어로 전멸 패배를 공통 판정한다.
	public abstract class StageModeBase : IStageMode
	{
		// RunAsync 가 클리어를 확정하면 Cleared 로 채운다. CheckResult 가 폴링.
		protected DungeonResult _result = DungeonResult.InProgress;

		public UniTask SetupAsync(Table_DungeonStage.Row stage, CancellationToken ct)
		{
			runGuardedAsync(stage, ct).Forget();
			return UniTask.CompletedTask;
		}

		// RunAsync 의 취소 예외를 흡수(미관측 예외 방지)하고, 종료 후 공통 정리를 보장한다.
		private async UniTaskVoid runGuardedAsync(Table_DungeonStage.Row stage, CancellationToken ct)
		{
			await RunAsync(stage, ct).SuppressCancellationThrow();
			OnFinished();
		}

		// 모드별 진행 루프 — 클리어 시 _result 를 Cleared 로 세팅하고 반환한다. ct 취소 시 자연 종료(throw).
		protected abstract UniTask RunAsync(Table_DungeonStage.Row stage, CancellationToken ct);

		// 모드 종료(클리어/취소) 시 공통 정리 — 이벤트 구독 해제 등. 필요 시 오버라이드.
		protected virtual void OnFinished()
		{
		}

		public DungeonResult CheckResult()
		{
			if (_result != DungeonResult.InProgress)
			{
				return _result;
			}

			if (AllHeroesDead() == true)
			{
				return DungeonResult.Defeat;
			}

			return DungeonResult.InProgress;
		}

		// 1회성 스폰 몬스터가 모두 정리됐는지 — WaitUntil 메서드 그룹용
		protected static bool AreMonstersCleared()
		{
			return MonsterSpawnManager.Instance.ActiveCount <= 0;
		}

		// 단계가 쓸 스폰 그룹. 신규 테이블은 단수 컬럼이라 그룹이 하나뿐이다.
		//
		// TODO(STEP 15) — 설계상 `MonsterSpawnGroupIDs` 는 배열(`!int[]`)이어야 하는데
		// 컨버터가 단수 int 로 생성했다. 배열이 되면 웨이브별로 다른 그룹을 쓸 수 있다.
		protected static int GetSpawnGroupId(Table_DungeonStage.Row stage)
		{
			return (stage != null) ? stage.MonsterSpawnGroupIDs : 0;
		}

		// 몬스터 레벨 — DungeonStage.MonsterLevel 이 있으면 MonsterSpawn.Level 을 오버라이드한다 (몬스터 설계 8장).
		protected static int GetLevelOverride(Table_DungeonStage.Row stage)
		{
			return (stage != null) ? stage.MonsterLevel : 0;
		}

		private static bool AllHeroesDead()
		{
			if (UnitManager.HasInstance == false)
			{
				return false;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			if (heroes.Count == 0)
			{
				return false;
			}

			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase h = heroes[i];
				if (h != null && h.IsDead == false)
				{
					return false;
				}
			}

			return true;
		}
	}
}
