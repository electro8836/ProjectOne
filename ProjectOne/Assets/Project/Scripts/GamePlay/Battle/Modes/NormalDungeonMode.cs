using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Map;
using ProjectOne.Unit;

namespace ProjectOne.Battle
{
	// 일반 던전 모드 — DungeonList 의 맵을 로드하고 DungeonStage 의 몬스터 구성으로 스폰한다.
	public sealed class NormalDungeonMode : IBattleMode
	{
		// 파티 배치 기준 위치(맵 좌측)와 세로 간격
		private static readonly Vector3 _partyBasePos = new Vector3(-4f, 0f, 0f);
		private const float _partySpacing = 1.5f;

		// 몬스터 스폰 영역(맵 우측)
		private static readonly Vector3 _monsterSpawnCenter = new Vector3(4f, 0f, 0f);
		private const float _monsterSpawnRadius = 2.5f;

		// 테이블에 SpawnCount 가 없을 때의 폴백 유지 수
		private const int _fallbackSpawnCount = 5;

		public async UniTask SetupAsync(BattleContext ctx, BattleDirector dir, CancellationToken ct)
		{
			Table_DungeonList.Row dungeon = Table_DungeonList.Get(ctx.DungeonId);
			if (dungeon == null)
			{
				Debug.LogError($"[NormalDungeonMode] Table_DungeonList.Get({ctx.DungeonId}) == null");
				return;
			}

			bool mapOk = await MapManager.Instance.LoadMapAsync(dungeon.MapPrefab, ct);
			if (mapOk == false)
			{
				return;
			}

			await SpawnPartyAsync(ctx, ct);
			RegisterMonsters(ctx);
		}

		public BattleResult CheckResult()
		{
			// 살아있는 히어로가 없으면 패배. 승리 조건(클리어)은 이번 범위 밖.
			if (UnitContainer.HasInstance == false)
			{
				return BattleResult.InProgress;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase h = heroes[i];
				if (h != null && h.IsDead == false)
				{
					return BattleResult.InProgress;
				}
			}

			return BattleResult.Defeat;
		}

		private async UniTask SpawnPartyAsync(BattleContext ctx, CancellationToken ct)
		{
			if (ctx.PartyCharacterIds == null)
			{
				return;
			}

			float centerOffset = (ctx.PartyCharacterIds.Length - 1) * 0.5f;
			for (int i = 0; i < ctx.PartyCharacterIds.Length; i++)
			{
				int characterId = ctx.PartyCharacterIds[i];
				if (characterId <= 0)
				{
					continue;
				}

				Vector3 pos = _partyBasePos + new Vector3(0f, (i - centerOffset) * _partySpacing, 0f);

				bool autoControl = true; //일단 자동스킬모드로 설정
				await UnitFactory.Instance.CreateHeroAsync(characterId, pos, Faction.Player, autoControl, ct);
			}
		}

		private void RegisterMonsters(BattleContext ctx)
		{
			Table_DungeonStage.Row stage = FindStage(ctx.DungeonId, ctx.Step);
			if (stage == null)
			{
				Debug.LogError($"[NormalDungeonMode] DungeonStage 없음: dungeon={ctx.DungeonId}, step={ctx.Step}");
				return;
			}

			int count = stage.SpawnCount > 0 ? stage.SpawnCount : _fallbackSpawnCount;

			RegisterGroup(stage.NormalMonsterIDs, count);
			RegisterGroup(stage.EliteMonsterIDs, count);
			if (stage.BossMonsterID > 0)
			{
				MonsterSpawnManager.Instance.RegisterSpawn(stage.BossMonsterID, _monsterSpawnCenter, _monsterSpawnRadius, 10, 10, 0f);
			}
		}

		private void RegisterGroup(int[] monsterIds, int count)
		{
			if (monsterIds == null)
			{
				return;
			}

			for (int i = 0; i < monsterIds.Length; i++)
			{
				int id = monsterIds[i];
				if (id > 0)
				{
					MonsterSpawnManager.Instance.RegisterSpawn(id, _monsterSpawnCenter, _monsterSpawnRadius, count, count, 2f);
				}
			}
		}

		// DungeonListID + Step 으로 스테이지 행 검색 (Dictionary 라 enumerator 직접 순회)
		private static Table_DungeonStage.Row FindStage(int dungeonId, int step)
		{
			Dictionary<int, Table_DungeonStage.Row>.ValueCollection.Enumerator e = Table_DungeonStage.All().Values.GetEnumerator();
			while (e.MoveNext())
			{
				Table_DungeonStage.Row row = e.Current;
				if (row.DungeonListID == dungeonId && row.Step == step)
				{
					return row;
				}
			}

			return null;
		}
	}
}
