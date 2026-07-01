using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Flow;
using ProjectOne.Map;
using ProjectOne.UI;
using ProjectOne.Unit;
using ProjectOne.Projectile;
using ProjectOne.Audio;
using ProjectOne.Utils;
using ProjectOne.UserData;

namespace ProjectOne.Dungeon
{
	// 던전 한 판의 오케스트레이터(씬 배치). 히어로 1회 스폰 → 스테이지 순차 진행(맵 교체/모드 실행/선택) →
	// 본편 클리어 후 엑스트라 도전(선택) → 정리 후 로비 복귀.
	public sealed class DungeonDirector : MonoBehaviour
	{
		// 임시: 캐릭터 미보유(CharacterId<=0) 시 사용할 기본 캐릭터 ID. 추후 신규계정 시작데이터로 대체.
		private const int TempFallbackCharacterId = 101;

		// 히어로 배치 기준 위치(맵 좌측)
		private static readonly Vector3 HeroBasePos = new Vector3(0f, 0f, 0f);

		[Header("UI 주소 (Addressable)")]
		[SerializeField] private string _stageSelectAddress = "Prefab_StageSelectPopup";
		[SerializeField] private string _confirmPopupAddress = "Prefab_StageFinishPopup";

		private Table_Dungeon.Row _dungeon;
		private DungeonContext _ctx;

		// 전투 수명 토큰 — 종료(클리어/패배/강제퇴장) 시 취소해 진행 루프를 결정적으로 중단한다.
		private CancellationTokenSource _cts;

		// 현재 진행 중인 스테이지/모드 (폴링 대상)
		private Table_Stage.Row _currentStage;
		private IStageMode _currentMode;

		private bool _ending;

		// 스테이지 선택 후보 수집용 버퍼 (재사용)
		private readonly List<int> _candidateBuffer = new List<int>();

		// 플로우필드 재베이크 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		// 진입점 — DungeonState 가 씬 로드 후 호출한다. 히어로 스폰·첫 스테이지 진입까지 await.
		public async UniTask Begin(DungeonContext ctx)
		{
			if (ctx == null)
			{
				Debug.LogError("[DungeonDirector] DungeonContext == null");
				return;
			}

			Table_Dungeon.Row dungeon = Table_Dungeon.Get(ctx.DungeonId);
			if (dungeon == null)
			{
				Debug.LogError($"[DungeonDirector] Table_Dungeon.Get({ctx.DungeonId}) == null");
				return;
			}

			if (ctx.CharacterId <= 0)
			{
				Debug.LogWarning($"[DungeonDirector] CharacterId 미설정 → 임시 기본값 {TempFallbackCharacterId} 사용");
				ctx.CharacterId = TempFallbackCharacterId;
			}

			_dungeon = dungeon;
			_ctx = ctx;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

			// 던전 런 상태 초기화 (임시재화 0) — 로그라이트 런 시작
			DungeonRunState.Instance.Reset();

			// 히어로 1회 스폰 (던전 전체 동안 유지)
			await spawnHeroAsync(ctx, _cts.Token);

			// 첫 스테이지: StageGroupID_1 그룹의 StageID_1 고정
			int firstStageId = firstStageOfGroup(dungeon.StageGroupID_1);
			bool entered = await beginStageAsync(firstStageId, _cts.Token);
			if (entered == false)
			{
				endDungeonAsync(false).Forget();
				return;
			}

			// 첫 스테이지 진행 폴링부터 던전 끝까지 백그라운드로 구동
			runDungeonGuardedAsync(_cts.Token).Forget();
		}

		// 승패 무관 강제 종료 (HUD ExitButton 등에서 호출)
		public void RequestExit()
		{
			if (_ending == true)
			{
				return;
			}

			_cts?.Cancel();
			endDungeonAsync(false).Forget();
		}

		private void Update()
		{
			updateFlowFieldBake();
		}

		private void OnDestroy()
		{
			_cts?.Dispose();
		}

		// 진행 루프의 취소 예외를 흡수(미관측 예외 방지)하고, 정상 종료 시에만 로비로 전이한다.
		private async UniTaskVoid runDungeonGuardedAsync(CancellationToken ct)
		{
			(bool canceled, bool victory) = await runStagesAsync(ct).SuppressCancellationThrow();
			if (canceled == true || ct.IsCancellationRequested == true)
			{
				return;
			}

			endDungeonAsync(victory).Forget();
		}

		// 본편 스테이지 순차 진행 + 엑스트라 도전. 본편 전부 클리어면 true(승리).
		private async UniTask<bool> runStagesAsync(CancellationToken ct)
		{
			// 첫 스테이지는 Begin 에서 이미 진입(_currentStage/_currentMode 세팅됨)
			for (int index = 0; index < _dungeon.TotalStageCount; index++)
			{
				DungeonResult result = await waitStageResultAsync(ct);
				if (ct.IsCancellationRequested == true)
				{
					return false;
				}

				cleanupStage();
				if (result == DungeonResult.Defeat)
				{
					return false;
				}

				int nextPosition = index + 1;
				if (nextPosition >= _dungeon.TotalStageCount)
				{
					break;
				}

				int nextStageId = await selectNextStageAsync(nextPosition, ct);
				if (ct.IsCancellationRequested == true)
				{
					return false;
				}

				bool entered = await beginStageAsync(nextStageId, ct);
				if (entered == false)
				{
					return false;
				}
			}

			// 본편 클리어 — 엑스트라 스테이지 도전(선택)
			await tryExtraStageAsync(ct);
			return true;
		}

		// 엑스트라 그룹이 있으면 도전 여부를 묻고, 수락 시 엑스트라 스테이지 1판을 진행한다.
		// (재화 차감은 흐름 외 — 추후 구현. 엑스트라 결과는 본편 승리에 영향 없음)
		private async UniTask tryExtraStageAsync(CancellationToken ct)
		{
			if (_dungeon.ExtraStageGroupID <= 0)
			{
				return;
			}

			bool challenge = await askExtraChallengeAsync(ct);
			if (challenge == false || ct.IsCancellationRequested == true)
			{
				return;
			}

			int extraStageId = firstStageOfGroup(_dungeon.ExtraStageGroupID);
			bool entered = await beginStageAsync(extraStageId, ct);
			if (entered == false)
			{
				return;
			}

			await waitStageResultAsync(ct);
			if (ct.IsCancellationRequested == true)
			{
				return;
			}

			cleanupStage();
		}

		// 스테이지 진입 — 맵 로드 + 모드 셋업. 성공 시 _currentStage/_currentMode 세팅 후 true.
		private async UniTask<bool> beginStageAsync(int stageId, CancellationToken ct)
		{
			Table_Stage.Row stage = Table_Stage.Get(stageId);
			if (stage == null)
			{
				Debug.LogError($"[DungeonDirector] Table_Stage.Get({stageId}) == null");
				return false;
			}

			// 스테이지 경계 — 리롤 가격을 최초 비용으로 리셋 (스테이지 진행 중 상점에는 누적)
			DungeonRunState.Instance.ResetRerollCount();

			// 다음 스테이지 진입 시 이전 인터미션에서 남은 기믹(상점 등) 일괄 회수
			if (GimmickService.HasInstance == true)
			{
				GimmickService.Instance.DespawnAll();
			}

			Table_MapData.Row mapData = Table_MapData.Get(stage.MapID);
			if (mapData == null)
			{
				Debug.LogError($"[DungeonDirector] Table_MapData.Get({stage.MapID}) == null");
				return false;
			}

			bool mapOk = await MapManager.Instance.LoadMapAsync(mapData.MapPrefab, ct);
			if (mapOk == false)
			{
				return false;
			}

			// 새 맵 기준 플로우필드 재베이크 트리거를 위해 캐시 셀 초기화
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

			IStageMode mode = StageModeFactory.Create(stage.ModeType);
			await mode.SetupAsync(stage, ct);

			_currentStage = stage;
			_currentMode = mode;

			// 스테이지 드랍 그룹 준비 (타입별 풀 사전 생성)
			if (stage.DropObjectGroupID > 0)
			{
				await DropManager.Instance.PrepareStageAsync(stage.DropObjectGroupID, ct);
			}

			Debug.Log($"[DungeonDirector] 스테이지 진입: {stage.Name} (모드 {stage.ModeType})");
			return true;
		}

		// 현재 스테이지 결과 폴링 — 클리어/패배 확정 또는 제한시간 초과(패배)까지 대기.
		private async UniTask<DungeonResult> waitStageResultAsync(CancellationToken ct)
		{
			float limit = _currentStage.LimitTime;
			float elapsed = 0f;
			while (ct.IsCancellationRequested == false)
			{
				DungeonResult result = _currentMode.CheckResult();
				if (result != DungeonResult.InProgress)
				{
					return result;
				}

				if (limit > 0f)
				{
					elapsed += Time.deltaTime;
					if (elapsed >= limit)
					{
						Debug.Log("[DungeonDirector] 제한시간 초과 → 패배");
						return DungeonResult.Defeat;
					}
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
			}

			return DungeonResult.InProgress;
		}

		// 다음 스테이지 선택 — 그룹에서 중복 없이 2개(없으면 1개) 뽑아 StageSelectUI 로 유저 선택.
		private async UniTask<int> selectNextStageAsync(int stagePosition, CancellationToken ct)
		{
			int groupId = stageGroupIdAt(_dungeon, stagePosition);
			Table_StageGroup.Row group = Table_StageGroup.Get(groupId);
			if (group == null)
			{
				Debug.LogError($"[DungeonDirector] Table_StageGroup.Get({groupId}) == null");
				return 0;
			}

			collectStageIds(group, _candidateBuffer);
			if (_candidateBuffer.Count == 0)
			{
				Debug.LogError($"[DungeonDirector] StageGroup({groupId}) 에 유효 스테이지 없음");
				return 0;
			}

			if (_candidateBuffer.Count == 1)
			{
				return _candidateBuffer[0];
			}

			// 중복 없이 2개 랜덤 추출
			int idxA = Random.Range(0, _candidateBuffer.Count);
			int stageA = _candidateBuffer[idxA];
			_candidateBuffer.RemoveAt(idxA);
			int stageB = _candidateBuffer[Random.Range(0, _candidateBuffer.Count)];

			if (string.IsNullOrEmpty(_stageSelectAddress))
			{
				return stageA;
			}

			// 유저가 OpenSelectButton 을 눌러 선택 UI 를 열고, 스테이지를 고를 때까지 반복.
			// 선택 UI 의 ExitButton(0 반환) 으로 닫으면 다시 OpenSelectButton 노출 단계로 돌아간다.
			BattleHUD hud = UIManager.Instance.GetScreen<BattleHUD>();
			while (ct.IsCancellationRequested == false)
			{
				if (hud != null)
				{
					await hud.WaitOpenSelectAsync(ct);
					if (ct.IsCancellationRequested == true)
					{
						return 0;
					}
				}

				StageSelectUI ui = await UIManager.Instance.OpenOverlayAsync<StageSelectUI>(_stageSelectAddress, ct);
				if (ui == null)
				{
					return stageA;
				}

				int chosen = await ui.WaitSelectionAsync(stageA, stageB, ct);
				await UIManager.Instance.CloseOverlayAsync(false);

				if (chosen > 0)
				{
					return chosen;
				}
			}

			return 0;
		}

		private async UniTask<bool> askExtraChallengeAsync(CancellationToken ct)
		{
			if (string.IsNullOrEmpty(_confirmPopupAddress))
			{
				return false;
			}

			return await UIManager.Instance.ShowConfirmPopupAsync(_confirmPopupAddress, "엑스트라 스테이지에 도전하시겠습니까?", ct);
		}

		private static async UniTask spawnHeroAsync(DungeonContext ctx, CancellationToken ct)
		{
			if (ctx.CharacterId <= 0)
			{
				Debug.LogError("[DungeonDirector] 캐릭터 로드 실패!");
				return;
			}

			// 플레이어 조작 히어로 1명 소환 (자동스킬 모드)
			await UnitFactory.Instance.CreateHeroAsync(ctx.CharacterId, HeroBasePos, Faction.Player, true, ct);
		}

		// 스테이지 종료 시 — 분할 소환 중단/잔존 몬스터만 제거(맵·히어로는 유지).
		// 맵은 다음 스테이지 진입의 LoadMapAsync 가 교체하고, 던전 종료 시 cleanupAll 이 언로드한다.
		private void cleanupStage()
		{
			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.ClearAlive();
			}

			_currentStage = null;
			_currentMode = null;
		}

		private async UniTaskVoid endDungeonAsync(bool victory)
		{
			if (_ending == true)
			{
				return;
			}

			_ending = true;
			Debug.Log($"[DungeonDirector] 던전 종료 victory={victory}");

			// 승리 시 던전 클리어 기록(카드스킬 해금 등 진행도 게이트에 사용)
			if (victory == true && _ctx != null)
			{
				Account.Instance.ClearedDungeons.MarkCleared(_ctx.DungeonId);
			}

			cleanupAll();
			await GameFlow.Instance.ChangeStateAsync(new LobbyState());
		}

		// 던전 종료 시 유닛/스폰/풀/맵 일괄 정리.
		// 전투씬 수명 매니저는 씬과 함께 파괴되지만, 영속(DontDestroyOnLoad) 매니저는 명시적으로 비운다.
		private void cleanupAll()
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

			// 드랍 풀 정리 + 프리팹 Addressable 핸들 해제
			if (DropManager.HasInstance == true)
			{
				DropManager.Instance.Clear();
			}

			// 기믹 인스턴스 회수 + 프리팹 Addressable 핸들 해제
			if (GimmickService.HasInstance == true)
			{
				GimmickService.Instance.Clear();
			}

			if (MapManager.HasInstance == true)
			{
				MapManager.Instance.UnloadMap();
			}

			// 영속(DontDestroyOnLoad) 매니저 — 씬과 함께 파괴되지 않아 풀/캐시를 명시적으로 비운다
			ProjectileManager.Instance.Clear();
			VFXManager.Instance.Clear();
			AudioManager.Instance.Clear();
		}

		// 기준 히어로의 셀 변경 시에만 플로우필드 재베이크
		private void updateFlowFieldBake()
		{
			if (MapManager.HasInstance == false || MapManager.Instance.HasMap == false)
			{
				return;
			}

			UnitBase hero = findFirstAliveHero();
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

		private static UnitBase findFirstAliveHero()
		{
			if (UnitContainer.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
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

		private static int firstStageOfGroup(int groupId)
		{
			Table_StageGroup.Row group = Table_StageGroup.Get(groupId);
			if (group == null)
			{
				Debug.LogError($"[DungeonDirector] Table_StageGroup.Get({groupId}) == null");
				return 0;
			}

			return group.StageID_1;
		}

		// 스테이지 진행 위치(0-based) → StageGroupID_(position+1)
		private static int stageGroupIdAt(Table_Dungeon.Row dungeon, int position)
		{
			switch (position)
			{
			case 0:
				return dungeon.StageGroupID_1;
			case 1:
				return dungeon.StageGroupID_2;
			case 2:
				return dungeon.StageGroupID_3;
			case 3:
				return dungeon.StageGroupID_4;
			case 4:
				return dungeon.StageGroupID_5;
			default:
				return 0;
			}
		}

		private static void collectStageIds(Table_StageGroup.Row group, List<int> output)
		{
			output.Clear();
			addStageId(output, group.StageID_1);
			addStageId(output, group.StageID_2);
			addStageId(output, group.StageID_3);
			addStageId(output, group.StageID_4);
			addStageId(output, group.StageID_5);
		}

		private static void addStageId(List<int> output, int stageId)
		{
			if (stageId > 0)
			{
				output.Add(stageId);
			}
		}
	}
}
