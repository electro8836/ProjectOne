using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Map;
using ProjectOne.Unit;

namespace ProjectOne.Battle
{
	// Table_MapInfo 기반 전투 모드 공통부 — 맵 로드 / 파티 스폰 / 패배 판정 / 클리어 대기를 제공한다.
	// 진행 규칙(웨이브 루프, 레이드 단일전)은 하위 클래스가 RunAsync 에서 구현한다.
	public abstract class MapBattleModeBase : IBattleMode
	{
		// 히어로 배치 기준 위치(맵 좌측)
		protected static readonly Vector3 HeroBasePos = new Vector3(-4f, 0f, 0f);

		// 몬스터 스폰 영역(맵 우측)
		protected static readonly Vector3 MonsterSpawnCenter = new Vector3(4f, 0f, 0f);
		protected const float MonsterSpawnRadius = 2.5f;

		// RunAsync 가 승패를 확정하면 채운다. CheckResult 가 폴링.
		protected BattleResult _result = BattleResult.InProgress;

		public async UniTask SetupAsync(BattleContext ctx, BattleDirector dir, CancellationToken ct)
		{
			Table_MapInfo.Row map = Table_MapInfo.Get(ctx.MapId);
			if (map == null)
			{
				Debug.LogError($"[MapBattleModeBase] Table_MapInfo.Get({ctx.MapId}) == null");
				return;
			}

			bool mapOk = await MapManager.Instance.LoadMapAsync(map.MapPrefab, ct);
			if (mapOk == false)
			{
				return;
			}

			await SpawnHeroAsync(ctx, ct);
			RunAsync(map, ct).Forget();
		}

		// 모드별 진행 루프 — 셋업 완료 후 백그라운드로 구동된다.
		protected abstract UniTaskVoid RunAsync(Table_MapInfo.Row map, CancellationToken ct);

		public BattleResult CheckResult()
		{
			if (_result != BattleResult.InProgress)
			{
				return _result;
			}

			if (AllHeroesDead() == true)
			{
				return BattleResult.Defeat;
			}

			return BattleResult.InProgress;
		}

		private static async UniTask SpawnHeroAsync(BattleContext ctx, CancellationToken ct)
		{
			if (ctx.CharacterId <= 0)
			{
				Debug.LogError("캐릭터 로드 실패!");
				return;
			}

			// 동료 없이 플레이어 조작 히어로 1명만 소환
			bool autoControl = true; // 일단 자동스킬모드로 설정
			await UnitFactory.Instance.CreateHeroAsync(ctx.CharacterId, HeroBasePos, Faction.Player, autoControl, ct);
		}

		// 현재 살아있는 몬스터(1회성 스폰)가 모두 정리됐는지 — WaitUntil 메서드 그룹용
		protected static bool AreMonstersCleared()
		{
			return MonsterSpawnManager.Instance.ActiveCount <= 0;
		}

		private static bool AllHeroesDead()
		{
			if (UnitContainer.HasInstance == false)
			{
				return false;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
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
