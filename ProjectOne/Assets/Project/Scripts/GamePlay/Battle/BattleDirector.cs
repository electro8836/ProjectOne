using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;
using ProjectOne.Map;
using ProjectOne.Unit;

namespace ProjectOne.Battle
{
	// 전투 한 판의 오케스트레이터(씬 배치). 모드 셋업 → 플로우필드 베이크 트리거 → 승패 폴링 → 정리.
	public sealed class BattleDirector : MonoBehaviour
	{
		private IBattleMode _mode;
		private bool _setupDone;
		private bool _ending;

		// 플로우필드 재베이크 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		// 진입점 — BattleState 가 씬 로드 후 호출한다.
		public void Begin(BattleContext ctx)
		{
			if (ctx == null)
			{
				Debug.LogError("[BattleDirector] BattleContext == null");
				return;
			}

			_mode = BattleModeFactory.Create(ctx.Mode);
			BeginAsync(ctx).Forget();
		}

		// 승패 무관 강제 종료 (HUD ExitButton 등에서 호출 가능)
		public void RequestExit()
		{
			EndBattle(BattleResult.InProgress).Forget();
		}

		private async UniTaskVoid BeginAsync(BattleContext ctx)
		{
			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			await _mode.SetupAsync(ctx, this, ct);
			_setupDone = true;
		}

		private void Update()
		{
			if (_mode == null || _ending == true)
			{
				return;
			}

			UpdateFlowFieldBake();
			UpdateResult();
		}

		// 구 MonsterAiCoordinator 로직 — 기준 히어로의 셀 변경 시에만 플로우필드 재베이크
		private void UpdateFlowFieldBake()
		{
			if (MapManager.HasInstance == false || MapManager.Instance.HasMap == false)
			{
				return;
			}

			UnitBase hero = FindFirstAliveHero();
			if (hero == null)
			{
				return;
			}

			Vector3Int currentCell = MapManager.Instance.WorldToCell(hero.transform.position);
			if (currentCell == _lastHeroCell)
			{
				return;
			}

			_lastHeroCell = currentCell;
			MapManager.Instance.BakeFlowField(hero.transform.position);
		}

		private void UpdateResult()
		{
			if (_setupDone == false)
			{
				return;
			}

			BattleResult result = _mode.CheckResult();
			if (result == BattleResult.InProgress)
			{
				return;
			}

			EndBattle(result).Forget();
		}

		private async UniTaskVoid EndBattle(BattleResult result)
		{
			if (_ending == true)
			{
				return;
			}

			_ending = true;
			Debug.Log($"[BattleDirector] 전투 종료: {result}");
			Cleanup();
			await GameFlow.Instance.ChangeStateAsync(new LobbyState());
		}

		// 유닛/스폰목록/맵 데이터 정리 (매니저 인스턴스는 DontDestroyOnLoad 라 다음 판에 재사용)
		private void Cleanup()
		{
			if (UnitContainer.HasInstance == true)
			{
				UnitContainer.Instance.ClearAll();
			}

			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.Clear();
			}

			if (MapManager.HasInstance == true)
			{
				MapManager.Instance.UnloadMap();
			}
		}

		private static UnitBase FindFirstAliveHero()
		{
			if (UnitContainer.HasInstance == false)
			{
				return null;
			}

			System.Collections.Generic.IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase h = heroes[i];
				if (h != null && h.IsDead == false)
				{
					return h;
				}
			}

			return null;
		}
	}
}
