using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 유적 던전 — 방어 시스템 무력화.
	//
	// 1) 전투방 — 움직이지 않는 코어(보스)와 방어장치가 있다. 코어의 HP 구간마다 방어 시스템이
	//    던전 전체에 공격을 순서대로 내리고, 구간 진입 시 가디언을 소환한다 (RuinsCoreSequencer).
	// 2) 코어(보스)를 파괴하면 남은 몬스터가 함께 사라지고 곧바로 클리어다. 보상은 처치 드랍 + 클리어 보상(서버)이다.
	//
	// 몬스터에게 죽는 것은 StageModeBase 의 공통 Defeat 판정 → 디렉터의 부활 팝업 경로를 그대로 탄다.
	// 제한시간은 디렉터가 판정한다.
	public sealed class RuinsDungeonMode : StageModeBase
	{
		private Table_RuinsDungeon.Row _row;
		private RuinsMap _map;
		private int[] _groups;

		private UnitBase _core;
		private readonly RuinsCoreSequencer _sequencer = new RuinsCoreSequencer();

		protected override async UniTask RunAsync(DungeonContext ctx, CancellationToken ct)
		{
			_row = DungeonProgress.FindRuinsStage(ctx.Stage);
			if (_row == null)
			{
				Debug.LogError($"[RuinsDungeonMode] RuinsDungeon 행이 없습니다 — Stage {ctx.Stage}");
				return;
			}

			_map = findMap();
			if (_map == null)
			{
				Debug.LogError("[RuinsDungeonMode] 맵에 RuinsMap 이 없습니다 — 맵 프리팹 루트에 붙여야 합니다.");
				return;
			}

			_groups = GetSpawnGroups(_row.MonsterSpawnGroupIDs);
			if (_groups.Length == 0)
			{
				Debug.LogError($"[RuinsDungeonMode] MonsterSpawnGroupIDs 가 비었습니다 — [0] 코어 그룹이 필요합니다. RuinsDungeon:{_row.ID}");
				return;
			}

			// 재도전은 씬을 유지한 채 맵만 바꾸므로 히어로가 이전 판 위치에 남아 있다 — 매번 시작 지점으로 옮긴다.
			placeHeroAtEntry();

			_core = await spawnCoreAsync();
			ct.ThrowIfCancellationRequested();
			if (_core == null)
			{
				Debug.LogError($"[RuinsDungeonMode] 코어를 소환하지 못했습니다 — 스폰 그룹 {_groups[0]}");
				return;
			}

			if (_groups.Length >= 2)
			{
				SpawnGroupRunner.SpawnGroupAt(_groups[1], _row.MonsterLevel, _map.DefenseSlots);
			}

			_sequencer.Setup(_core, _map, _row.CorePhaseGroupID, _groups, _row.MonsterLevel);

			while (_result == DungeonResult.InProgress)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, ct);

				// 사망 연출·부활 팝업 동안에는 방어 시스템도 멈춘다 — 살아나자마자 맞으면 안 된다.
				if (findAliveHero() == null)
				{
					continue;
				}

				if (_core.IsDead == true)
				{
					onCoreDestroyed();
					break;
				}

				_sequencer.Tick(Time.deltaTime);
			}
		}

		protected override void OnFinished()
		{
			_sequencer.Cancel();
		}

		// ── 코어 ──────────────────────────────────────────────────────

		// 코어 그룹의 첫 행을 코어로 본다 — 개체를 직접 받아야 HP 구간과 파괴를 볼 수 있다.
		private async UniTask<UnitBase> spawnCoreAsync()
		{
			IReadOnlyList<Table_MonsterSpawn.Row> rows = MonsterCatalog.GetSpawnGroup(_groups[0]);
			if (rows.Count == 0)
			{
				return null;
			}

			if (rows.Count > 1)
			{
				Debug.LogWarning($"[RuinsDungeonMode] 코어 스폰 그룹 {_groups[0]} 에 행이 {rows.Count}개입니다 — 첫 행만 코어로 소환합니다.");
			}

			Table_MonsterSpawn.Row row = rows[0];
			Vector3 pos = (_map.CoreSlot != null) ? _map.CoreSlot.Position : _map.transform.position;
			return await MonsterSpawnManager.Instance.SpawnOneShotAsync(row.MonsterID, _row.MonsterLevel, pos, row.RewardGroupID);
		}

		// 방어 시스템 무력화 = 클리어. 남은 몬스터는 처치가 아닌 풀 반환으로 걷는다(경험치·킬카운트 없음).
		private void onCoreDestroyed()
		{
			_sequencer.Cancel();

			// 결과창을 기다리는 동안 제한시간이 흐르지 않게 한다.
			if (DungeonDirector.HasInstance == true)
			{
				DungeonDirector.Instance.StopTimer();
			}

			MonsterSpawnManager.Instance.ClearAlive();
			_result = DungeonResult.Cleared;
		}

		// ── 공통 ──────────────────────────────────────────────────────

		private static RuinsMap findMap()
		{
			if (MapManager.HasInstance == false || MapManager.Instance.Current == null)
			{
				return null;
			}

			return MapManager.Instance.Current.GetComponent<RuinsMap>();
		}

		private void placeHeroAtEntry()
		{
			UnitBase hero = findAliveHero();
			if (hero == null)
			{
				return;
			}

			hero.transform.position = _map.EntryPosition;
			hero.RefreshFrameCache();
		}

		private static UnitBase findAliveHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && hero.IsDead == false)
				{
					return hero;
				}
			}

			return null;
		}
	}
}
