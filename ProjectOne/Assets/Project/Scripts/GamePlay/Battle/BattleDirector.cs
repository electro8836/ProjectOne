using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Flow;
using ProjectOne.Map;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.Unit;

namespace ProjectOne.Battle
{
	// 전투 한 판의 오케스트레이터(씬 배치). 모드 셋업 → 플로우필드 베이크 트리거 → 승패 폴링 → 정리.
	public sealed class BattleDirector : MonoBehaviour
	{
		// 임시: 캐릭터 미보유(CharacterId<=0) 시 사용할 기본 캐릭터 ID. 추후 신규계정 시작데이터로 대체.
		private const int TempFallbackCharacterId = 101;

		private IBattleMode _mode;
		private bool _setupDone;
		private bool _ending;
		private int[] _clearRewardIds;
		private int _characterId;
		private int _mapId;

		// 메인HUD 스킵 버튼이 발행하는 WaveSkipRequestedEvent 구독 캐시
		private Action<WaveSkipRequestedEvent> _onSkipRequested;

		// 전투 수명 토큰 — 전투 종료(승/패/강제퇴장) 시 즉시 취소해 모드 루프를 결정적으로 중단한다.
		private CancellationTokenSource _cts;

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

			Table_MapInfo.Row map = Table_MapInfo.Get(ctx.MapId);
			if (map == null)
			{
				Debug.LogError($"[BattleDirector] Table_MapInfo.Get({ctx.MapId}) == null");
				return;
			}

			// 임시: 캐릭터 미보유(0) 보정. 같은 ctx 가 SpawnHeroAsync 로 전달돼 스폰·보상 모두 적용됨.
			if (ctx.CharacterId <= 0)
			{
				Debug.LogWarning($"[BattleDirector] CharacterId 미설정 → 임시 기본값 {TempFallbackCharacterId} 사용");
				ctx.CharacterId = TempFallbackCharacterId;
			}

			_clearRewardIds = map.ClearRewardIDs;
			_characterId = ctx.CharacterId;
			_mapId = ctx.MapId;
			_mode = BattleModeFactory.Create(map.BattleType);
			BeginAsync(ctx).Forget();
		}

		// 승패 무관 강제 종료 (HUD ExitButton 등에서 호출 가능)
		public void RequestExit()
		{
			EndBattle(BattleResult.InProgress).Forget();
		}

		// 메인HUD 스킵 버튼 → 웨이브 대기 즉시 종료 (웨이브 모드일 때만 유효)
		public void RequestSkipWaveWait()
		{
			if (_mode is WaveMode wave)
			{
				wave.RequestSkipWait();
			}
		}

		private void OnEnable()
		{
			_onSkipRequested = OnSkipRequested;
			EventManager.Instance.Subscribe<WaveSkipRequestedEvent>(_onSkipRequested);
		}

		private void OnDisable()
		{
			EventManager.Instance.Unsubscribe<WaveSkipRequestedEvent>(_onSkipRequested);
		}

		private void OnSkipRequested(WaveSkipRequestedEvent evt)
		{
			RequestSkipWaveWait();
		}

		private async UniTaskVoid BeginAsync(BattleContext ctx)
		{
			// 파괴 토큰에 연결한 전투용 CTS — EndBattle 에서 명시 취소(파괴 전이라도 즉시 중단)
			_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
			await _mode.SetupAsync(ctx, this, _cts.Token);
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

			// 모드 루프(RunAsync)를 먼저 중단 — Cleanup/씬전환 전이라 잔존 루프의 오작동(웨이브 오진행)을 막는다
			_cts?.Cancel();

			Debug.Log($"[BattleDirector] 전투 종료: {result}");

			// 승패 확정 시에만 결과/보상 처리 (강제 종료=InProgress 는 제외)
			if (result != BattleResult.InProgress)
			{
				bool victory = result == BattleResult.Victory;
				if (victory == true)
				{
					// (테스트) 서버 권위 던전클리어 — 서버가 exp 가산 저장 후 반환. 결과는 콜백에서 처리.
					NetworkManager.Instance.RequestDungeonClear(new DungeonClearRequest { mapId = _mapId }, onDungeonClearResult);
				}

				EventManager.Instance.Publish(new BattleEndedEvent(victory, _clearRewardIds));
			}

			Cleanup();
			await GameFlow.Instance.ChangeStateAsync(new LobbyState());
		}

		// (테스트) 던전 클리어 서버 응답 — 서버 저장 exp 수신 확인.
		private void onDungeonClearResult(bool isSuccess, DungeonClearResponse data, string errorMsg)
		{
			if (isSuccess == true && data != null)
			{
				Debug.Log($"[테스트] DungeonClear 결과 → exp={data.exp}");
			}
		}

		private void OnDestroy()
		{
			_cts?.Dispose();
		}

		// 전투 종료 시 유닛/스폰목록/맵 데이터 정리 (매니저는 전투씬과 함께 파괴됨)
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

			// 몬스터 풀은 UnitContainer(전투씬 수명) 자식이라 씬과 함께 파괴됨 → 영속 허브 캐시를 무효화
			MonsterPoolHub.Instance.Clear();

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
