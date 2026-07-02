using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 보스전 모드 — MonsterSpawnGroupIDs[0] 그룹으로 보스를 소환하고, ClearValue 만큼의 보스 처치 시 클리어.
	// (보통 1마리지만 쌍둥이 보스 등은 ClearValue 2 이상)
	public sealed class BossBattleStageMode : StageModeBase
	{
		private int _killTarget;
		private int _killCount;
		private Action<UnitDiedEvent> _onUnitDied;

		protected override async UniTask RunAsync(Table_Stage.Row stage, CancellationToken ct)
		{
			_killTarget = Mathf.Max(1, stage.ClearValue);
			_killCount = 0;
			_onUnitDied = onUnitDied;
			EventManager.Instance.Subscribe<UnitDiedEvent>(_onUnitDied);

			int groupId = GetGroupId(stage.MonsterSpawnGroupIDs, 0);
			SpawnGroupRunner.SpawnGroup(groupId, ct);

			await UniTask.WaitUntil(isKillTargetReached, PlayerLoopTiming.Update, ct);

			// 스테이지 종료 상점은 DungeonDirector 가 본편 클리어 시 스폰한다(엑스트라 제외).
			_result = DungeonResult.Cleared;
		}

		private bool isKillTargetReached()
		{
			return _killCount >= _killTarget;
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (evt.UnitType != UnitType.Monster)
			{
				return;
			}

			Table_Monster.Row row = Table_Monster.Get(evt.TableID);
			if (row != null && row.MonsterType == MonsterTypes.Boss)
			{
				_killCount++;
			}
		}

		protected override void OnFinished()
		{
			if (_onUnitDied != null)
			{
				EventManager.Instance.Unsubscribe<UnitDiedEvent>(_onUnitDied);
				_onUnitDied = null;
			}
		}
	}
}
