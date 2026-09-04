using System;
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

		// 단계가 쓸 스폰 그룹들. **배열 순서가 곧 웨이브 순서**다 (몬스터 설계 8장).
		protected static int[] GetSpawnGroups(Table_DungeonStage.Row stage)
		{
			if (stage == null || stage.MonsterSpawnGroupIDs == null)
			{
				return Array.Empty<int>();
			}

			return stage.MonsterSpawnGroupIDs;
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
