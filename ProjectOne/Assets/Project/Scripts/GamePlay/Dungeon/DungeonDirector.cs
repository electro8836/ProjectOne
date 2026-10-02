using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Flow;
using ProjectOne.Items;
using ProjectOne.Loading;
using ProjectOne.Map;
using ProjectOne.UI;
using ProjectOne.Unit;
using ProjectOne.Projectile;
using ProjectOne.Audio;
using ProjectOne.Utils;
using ProjectOne.UserData;
using ProjectOne.Network;
using ProjectOne.Field;
using ProjectOne.Shared;
using ProjectOne.Summons;

namespace ProjectOne.Dungeon
{
	// 던전 한 판의 오케스트레이터(씬 배치).
	//
	// **1회 입장 = DungeonStage 1단계**다 (맵 설계 9장). 맵 로드 → 히어로 스폰 → 모드 실행 → 결과.
	// 여러 단계를 연달아 진행하지 않으며, "다음 단계 도전"은 결과창에서 새 입장으로 다시 들어온다.
	public sealed class DungeonDirector : MonoBehaviour
	{
		// 히어로 배치 기준 위치
		private static readonly Vector3 HeroBasePos = new Vector3(0f, 0f, 0f);

		// UI 프리팹 주소(Addressable) — 씬 직렬화로 숨는 것을 막기 위해 코드 상수로 고정
		private const string DungeonResultAddress = "UIPrefab_DungeonResult";
		private const string ContinuePopupAddress = "UIPrefab_ContinuePopup";

		// 종료 메시지(클리어·실패)를 띄운 뒤 결과창까지의 최소 시간(초).
		// 서버 응답이 이보다 늦으면 응답이 올 때까지 메시지를 띄운 채 기다린다.
		private const float ResultDelaySeconds = 3f;

		private Table_Dungeon.Row _dungeon;
		private DungeonContext _ctx;

		// 이번 판의 맵. 단계 테이블이 던전마다 달라 행 대신 맵 ID 만 들고 있는다.
		private int _mapId;

		// 균열 정산 시 확정한 최고 통과 웨이브. 결과창 표시용이다.
		private int _riftClearedWave;

		// 전투 수명 토큰 — 종료(클리어/패배/강제퇴장) 시 취소해 진행 루프를 결정적으로 중단한다.
		private CancellationTokenSource _cts;

		private IStageMode _currentMode;
		private bool _ending;

		// 클리어 서버 요청을 이미 시작했는지(중복 전송 방지) + 진행 중 요청 핸들.
		private bool _clearRequestSent;
		private UniTask<DungeonClearResponse> _clearTask;
		private UniTaskCompletionSource<DungeonClearResponse> _clearTcs;

		// 사망창에서 '귀환'을 선택했는지 — true 면 결과창 없이 즉시 마을로 복귀(보상 없음).
		private bool _forcedLobbyReturn;

		// 이번 판에 지급한 장비 인스턴스. 결과창이 등급·레벨·품질을 여기서 읽는다.
		//
		// GrantedRewardDto 에는 등급·품질이 없어(설계 STEP 14 한계) itemId 만으로는 슬롯을 채울 수 없다.
		// 지급할 때 만든 인스턴스를 그대로 넘겨야 화면과 인벤토리가 어긋나지 않는다.
		private readonly List<EquipmentInstance> _grantedEquipments = new List<EquipmentInstance>();

		// 이번 단계의 남은 제한시간(초). 0 이하로 떨어지면 실패다.
		//
		// 제한시간은 웨이브 진행 규칙이 아니라 던전 한 판의 성격이므로 모드가 아니라 여기가 소유한다.
		// HUD 도 여기서 읽는다 — 모드와 UI 가 각자 타이머를 돌리면 조용히 어긋난다.
		private float _remainTime;

		// TimeLimit 이 0(무제한)인 단계에서는 시간을 세지 않는다.
		private bool _hasTimeLimit;

		// 이번 단계 시작 시점의 계정 누적 경험치. 결과창에 보여줄 획득량의 기준선이다.
		private int _expAtStageStart;

		private bool _timedOut;

		// 사망 연출·팝업 동안 남은시간을 멈춘다.
		//
		// timeScale 0 만으로 멈추던 것을 플래그로 바꾼 이유 — 사망 연출 3초는 timeScale 이 1이어야
		// 사망 애니메이션과 카메라 줌이 돈다. 그 구간에도 타이머는 멈춰 있어야 한다.
		private bool _timerPaused;

		// 모드가 이 판의 제한시간을 끝낸 상태(유적 코어 파괴 등). 사망/부활이 켜고 끄는 _timerPaused 와 따로 둔다 —
		// 부활하면서 _timerPaused 가 풀려도 다시 흐르면 안 된다.
		private bool _timerStopped;

		// 플로우필드 재베이크 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		private static DungeonDirector _instance;

		public static bool HasInstance => _instance != null;

		public static DungeonDirector Instance => _instance;

		// 남은 제한시간(초). 무제한 단계면 0 이다.
		public float RemainTime => _remainTime;

		// 이 판의 남은 시간을 지금 값에 고정한다. 다음 단계·재도전(startStage)에서 풀린다.
		public void StopTimer()
		{
			_timerStopped = true;
		}

		// 진행 중인 던전 종류. 아직 Begin 전이면 None 이다.
		public EDT.Dungeon DungeonType => (_ctx != null) ? _ctx.DungeonType : EDT.Dungeon.None;

		// 진행 중인 단계. 아직 Begin 전이면 0 이다.
		public int Stage => (_ctx != null) ? _ctx.Stage : 0;

		// 진행 중인 모드. 모드별 HUD 가 진행 상태를 읽는다. 없으면 null.
		public IStageMode CurrentMode => _currentMode;

		// 던전 씬은 비어 있으므로 코드가 직접 생성한다.
		public static DungeonDirector EnsureInstance()
		{
			if (_instance != null)
			{
				return _instance;
			}

			GameObject go = new GameObject("DungeonDirector");
			_instance = go.AddComponent<DungeonDirector>();

			// 처치 경험치 지급기는 이벤트 구독형이라 킬이 나기 전에 살아 있어야 한다.
			ProjectOne.Monsters.MonsterKillReward.Instance.Touch();
			return _instance;
		}

		// 진입점 — DungeonState 가 씬 로드 후 호출한다. 맵 로드·히어로 스폰까지 await.
		public async UniTask Begin(DungeonContext ctx)
		{
			if (ctx == null)
			{
				Debug.LogError("[DungeonDirector] DungeonContext 가 null");
				return;
			}

			Table_Dungeon.Row dungeon = Table_Dungeon.Get(ctx.DungeonType);
			if (dungeon == null)
			{
				Debug.LogError($"[DungeonDirector] Table_Dungeon.Get({ctx.DungeonType}) == null");
				return;
			}

			int mapId = DungeonProgress.GetMapId(ctx.DungeonType, ctx.Stage);
			if (mapId <= 0)
			{
				Debug.LogError($"[DungeonDirector] 던전 단계 없음 — {ctx.DungeonType} Stage {ctx.Stage}");
				return;
			}

			_dungeon = dungeon;
			_mapId = mapId;
			_ctx = ctx;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

			DungeonRunState.Instance.Reset();

			// 상자 원장 — 서버가 입장 때 발급한 런 시드로 상자를 굴린다(없으면 로컬 런).
			DungeonRunLedger.Instance.Begin(ctx.Run);

			// 이전 씬(마을·필드)의 유닛과 풀을 걷어낸다 — 세 디렉터가 같은 규약을 쓴다.
			// 풀 오브젝트는 씬 컨테이너 아래에 살아 씬과 함께 파괴되므로, 비우지 않으면
			// MonsterPoolHub 가 죽은 풀을 재사용해 스폰이 조용히 실패한다.
			Map.GameplaySceneSetup.ClearGameplayUnits();

			// 카메라 리그는 씬과 함께 파괴된다. 없으면 vcam 이 없어 화면이 히어로를 따라가지 않는다.
			await Map.GameplaySceneSetup.EnsureCameraAsync(_cts.Token);

			await loadMapAsync(_cts.Token);
			await spawnHeroAsync(_cts.Token);

			// 월드 오브젝트 풀은 첫 처치 전에 준비돼 있어야 한다 — 없으면 보상 드랍이 유실된다.
			await DropManager.Instance.PrepareAsync(_cts.Token);

			// 유닛 위에 뜨는 게이지(보스 캐스팅·상호작용 진행)를 Canvas_World 에 올린다.
			await UIManager.Instance.EnsureWorldGaugeAsync(_cts.Token);

			// 던전 전용 HUD(웨이브 배너 등)는 startStage 앞에 세운다 — 모드가 시작하자마자 1번 웨이브를
			// 알리므로, 뒤에 두면 첫 배너를 통째로 놓친다.
			await UIManager.Instance.EnsureDungeonHudAsync(_ctx.DungeonType, _cts.Token);

			// 던전 입장은 항상 완전한 상태에서 시작한다 (기반테이블 5.3)
			healAllHeroes();

			startStage();

			// 진행 감시는 백그라운드로 — 여기서 await 를 끝내야 로딩 화면이 걷힌다.
			runGuardedAsync(_cts.Token).Forget();
		}

		// HUD 나가기 버튼 → 즉시 종료(보상 없음)
		public void RequestExit()
		{
			_forcedLobbyReturn = true;
			endDungeonAsync(false).Forget();
		}

		private void Update()
		{
			updateRemainTime();
			updateFlowFieldBake();
		}

		// 제한시간 카운트다운. 0 에 닿으면 진행 루프가 다음 프레임에 실패로 종료한다.
		private void updateRemainTime()
		{
			if (_hasTimeLimit == false || _timedOut == true || _ending == true || _timerPaused == true || _timerStopped == true)
			{
				return;
			}

			_remainTime -= Time.deltaTime;
			if (_remainTime > 0f)
			{
				return;
			}

			_remainTime = 0f;
			_timedOut = true;
			Debug.Log($"[DungeonDirector] 제한시간 초과 — {_ctx.DungeonType} Stage {_ctx.Stage}");
		}

		private void OnDestroy()
		{
			if (_instance == this)
			{
				_instance = null;
			}

			if (_cts != null)
			{
				_cts.Cancel();
				_cts.Dispose();
				_cts = null;
			}

			// 정상 종료(cleanupAll)를 거치지 않고 씬이 바뀌는 경로(전투 중 마을 이동 등)에서도 던전 HUD 를 걷는다.
			// UIManager 가 영속이라 여기서 놓치면 마을까지 따라간다. 이미 걷혔으면 아무것도 하지 않는다.
			if (UIManager.HasInstance == true)
			{
				UIManager.Instance.ReleaseDungeonHud();
			}
		}

		// ── 진행 ──────────────────────────────────────────────────────

		private async UniTaskVoid runGuardedAsync(CancellationToken ct)
		{
			(bool cancelled, bool victory) = await runStageAsync(ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			endDungeonAsync(victory).Forget();
		}

		private async UniTask<bool> runStageAsync(CancellationToken ct)
		{
			while (true)
			{
				DungeonResult result = _currentMode != null ? _currentMode.CheckResult() : DungeonResult.InProgress;
				if (result == DungeonResult.Cleared)
				{
					return true;
				}

				// 균열은 제한시간 초과도 한 판의 정상 종료다 — 거기까지 통과한 웨이브로 정산한다.
				if (_timedOut == true && _ctx.DungeonType == EDT.Dungeon.Rift)
				{
					return true;
				}

				// 제한시간 초과는 부활로 되돌릴 수 있는 상태가 아니다 — 왜 끝났는지만 알리고 마을로 보낸다.
				if (result == DungeonResult.Failed || _timedOut == true)
				{
					await showContinueAsync(buildFailedPopupData(), ct);
					return false;
				}

				if (result == DungeonResult.Defeat)
				{
					// 부활을 선택하면 같은 단계를 계속 진행한다.
					bool revived = await handleDefeatAsync(ct);
					if (revived == false)
					{
						return false;
					}
				}

				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}
		}

		// 되돌릴 수 없는 실패의 안내 문구 — 미궁 불이면 전용 문구, 그 외에는 시간 초과다.
		private ContinuePopupData buildFailedPopupData()
		{
			LabyrinthDungeonMode labyrinth = _currentMode as LabyrinthDungeonMode;
			if (labyrinth != null && labyrinth.IsFireDeath == true)
			{
				return ContinuePopupData.ForLabyrinthFire();
			}

			return ContinuePopupData.ForTimeout();
		}

		private void startStage()
		{
			// 결과창의 "이번 판 획득 경험치"는 이 스냅샷과의 차이다 — 누적 집계 경로가 따로 없다.
			// Loadout.Exp 는 늘기만 하고 줄지 않는다(ApplyLevelup 은 아직 호출부가 없다).
			// 레벨업이 붙어 경험치를 되돌리게 되면 이 뺄셈을 다시 봐야 한다.
			_expAtStageStart = currentExp();

			_hasTimeLimit = _dungeon.TimeLimit > 0;
			_remainTime = _hasTimeLimit ? _dungeon.TimeLimit : 0f;
			_timedOut = false;
			_timerPaused = false;
			_timerStopped = false;

			EventManager.Instance.Publish(new DungeonStageStartedEvent(_ctx.DungeonType, _ctx.Stage));

			_currentMode = StageModeFactory.Create(_ctx.DungeonType);
			if (_currentMode == null)
			{
				return;
			}

			_currentMode.SetupAsync(_ctx, _cts.Token).Forget();
		}

		private async UniTask loadMapAsync(CancellationToken ct)
		{
			// 던전은 단계마다 그리드맵 하나만 쓴다. 다음 단계로 넘어가면 여기서 교체된다.
			await MapManager.Instance.LoadMapAsync(_mapId, ct);
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);
		}

		private static async UniTask spawnHeroAsync(CancellationToken ct)
		{
			await UnitFactory.Instance.CreateHeroAsync(HeroBasePos, Faction.Player, true, ct);
		}

		// ── 사망 / 부활 ───────────────────────────────────────────────

		// 부활을 선택하면 true. 나가기이거나 부활 불가면 false.
		//
		// 부활 횟수가 없어도 팝업을 띄운다 — 왜 못 살아나는지 알려주고 나가기를 누르게 한다.
		private async UniTask<bool> handleDefeatAsync(CancellationToken ct)
		{
			// 연출이 끝나고 팝업을 고르는 동안 남은시간은 흐르지 않는다.
			_timerPaused = true;

			await DeathSequence.PlayAsync(ct);

			ContinuePopupData data = ContinuePopupData.ForDungeonDeath(_dungeon, DungeonRunState.Instance.ReviveUsedCount);
			ContinueChoice choice = await showContinueAsync(data, ct);

			if (choice != ContinueChoice.Retry)
			{
				// 마을로 나간다 — 씬이 바뀌므로 줌은 되돌릴 필요가 없다.
				_forcedLobbyReturn = true;
				return false;
			}

			DeathSequence.ResetZoom();
			_timerPaused = false;

			DungeonRunState.Instance.IncrementReviveUsed();
			reviveHeroes();
			return true;
		}

		// 사망 지점에서 부활 — 부활은 상황을 가리지 않고 전체 회복이다 (맵 설계 7.2).
		private static void reviveHeroes()
		{
			if (UnitManager.HasInstance == false)
			{
				return;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero == null || hero.IsDead == false)
				{
					continue;
				}

				hero.OnSpawnReset(hero.transform.position);
				EventManager.Instance.Publish(new UnitSpawnedEvent(hero, UnitType.Hero, hero.GetID(), 0));
			}
		}

		private static void healAllHeroes()
		{
			if (UnitManager.HasInstance == false)
			{
				return;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && hero.Vitals != null)
				{
					hero.Vitals.FullHeal();
				}
			}
		}

		// 팝업이 떠 있는 동안 게임을 멈춘다.
		//
		// 남은 제한시간은 updateRemainTime 이 Time.deltaTime 으로 깎으므로 timeScale 0 만으로 함께 멈춘다 —
		// 정지 플래그를 따로 두면 두 벌이 되어 한쪽만 풀리는 사고가 난다.
		private async UniTask<ContinueChoice> showContinueAsync(ContinuePopupData data, CancellationToken ct)
		{
			Time.timeScale = 0f;

			ContinueChoice choice = ContinueChoice.Exit;
			ContinuePopup popup = await UIManager.Instance.OpenWindowAsync<ContinuePopup>(ContinuePopupAddress, ct);
			if (popup != null)
			{
				choice = await popup.ShowAsync(data, ct);
				await UIManager.Instance.CloseWindowAsync(false);
			}

			Time.timeScale = 1f;
			return choice;
		}

		// ── 종료 ──────────────────────────────────────────────────────

		private async UniTaskVoid endDungeonAsync(bool victory)
		{
			if (_ending == true)
			{
				return;
			}

			_ending = true;
			Debug.Log($"[DungeonDirector] 던전 종료 victory={victory} — {_ctx.DungeonType} Stage {_ctx.Stage}");

			// 결과창에서 재도전·다음 단계를 고르면 다시 들어갈 컨텍스트. 없으면 마을로 간다.
			DungeonContext restart = null;

			if (victory == true && _forcedLobbyReturn == false)
			{
				// 이전 판(다음 단계 재진입)의 지급 장비가 결과창에 섞이지 않게 비운다.
				_grantedEquipments.Clear();

				// 최고 단계 갱신·퀘스트 이벤트는 서버 응답을 기다리지 않는다(서버도 같은 값으로 저장한다).
				markVictory();

				// 바닥 드랍 — 미궁은 남은 것을 지우고, 나머지는 모두 획득한다. 정산 요청 전이어야
				// 처치 원장이 모두 확정돼 이 요청 직전의 필드 배치에 함께 실린다.
				settleGroundDrops();

				// 종료 메시지를 띄워 두고 서버 응답을 기다린다 — 두 시계가 같이 흘러야 한다.
				// 응답이 빨라도 메시지 시간이 끝날 때까지, 시간이 끝나도 응답이 올 때까지 기다린다.
				showEndMessage();
				UniTask delay = UniTask.Delay(System.TimeSpan.FromSeconds(ResultDelaySeconds), cancellationToken: _cts.Token);

				DungeonClearResponse clear;
				if (DungeonRunLedger.Instance.IsServerRun == true)
				{
					beginClearRequestIfNeeded();
					clear = await _clearTask;
					applyClearResponse(clear);
				}
				else
				{
					// 로컬 런(미로그인·개발용 이동) — 클리어 보상만 로컬로 지급한다. 골드는 서버 없이는 보상이 없다.
					clear = settleLocal();
				}

				await delay;

				restart = await showDungeonResultAsync(buildResultResponse(clear), _cts.Token);
			}
			else
			{
				// 실패·강제 귀환도 런을 정산한다 — 주운 상자 보상은 결과와 무관하게 남는다.
				sendDungeonClearFailLog();
			}

			cleanupAll();

			// 재도전·다음 단계는 처음 입장과 같은 경로다 — 씬을 다시 로드하고 히어로·펫·맵·HUD 를 전부 새로 세운다.
			// 판 안에서 잔존물을 골라 치우면 빠뜨리는 것이 생긴다.
			if (restart != null)
			{
				await GameFlow.Instance.ChangeStateAsync(new DungeonState(restart));
				return;
			}

			await GameFlow.Instance.ChangeStateAsync(new TownState());
		}

		// 결과창 — 서버 확정 보상을 보여주고, 다음 단계가 있으면 도전 여부를 묻는다.
		// 재도전·다음 단계를 고르면 다시 들어갈 컨텍스트를, 마을로 나가야 하면 null 을 돌려준다.
		private async UniTask<DungeonContext> showDungeonResultAsync(DungeonClearResponse resp, CancellationToken ct)
		{
			DungeonResultUI ui = await UIManager.Instance.OpenWindowAsync<DungeonResultUI>(DungeonResultAddress, ct);
			if (ui == null)
			{
				return null;
			}

			IReadOnlyList<GrantedRewardDto> rewards = (resp != null) ? resp.rewards : null;

			// 입장 횟수는 결과창이 직접 읽는다 — 여기서는 다음 단계가 존재하는지만 알려준다.
			bool hasNext = DungeonProgress.HasNextStage(_ctx.DungeonType, _ctx.Stage);

			// 균열은 통과한 웨이브를 단계 자리에 보여준다.
			bool isRift = _ctx.DungeonType == EDT.Dungeon.Rift;
			int shownStage = isRift ? _riftClearedWave : _ctx.Stage;

			int gainedExp = currentExp() - _expAtStageStart;
			if (gainedExp < 0)
			{
				gainedExp = 0;
			}

			DungeonResultAction action = await ui.WaitAsync(rewards, _grantedEquipments,
				_ctx.DungeonType, shownStage, gainedExp, hasNext, ct);

			// 마을 복귀는 던전 로딩을 띄우지 않는다 — 마을행에 던전 로딩을 거는 건 어색하다.
			// 다만 창은 반드시 닫는다. cleanupAll 은 윈도우를 걷지 않아서 그대로 두면 마을까지 따라간다.
			if (action == DungeonResultAction.ReturnTown)
			{
				await UIManager.Instance.CloseWindowAsync(false);
				return null;
			}

			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToDungeon, ct);
			await UIManager.Instance.CloseWindowAsync(false);

			int stage = (action == DungeonResultAction.NextStage) ? _ctx.Stage + 1 : _ctx.Stage;

			// 균열 재도전은 방금 갱신된 기록의 체크포인트에서 시작한다.
			if (isRift == true)
			{
				stage = DungeonProgress.GetRiftCheckpoint();
			}

			if (DungeonProgress.GetMapId(_ctx.DungeonType, stage) <= 0)
			{
				Debug.LogError($"[DungeonDirector] 다시 들어갈 단계가 없습니다 — {_ctx.DungeonType} Stage {stage}");
				return null;
			}

			// 재도전도 다음 단계도 새 입장이다 — 서버가 횟수를 차감하고 새 런을 준다. 못 쓰면 그대로 마을로 나간다.
			DungeonEnterResult enter = await DungeonEntry.RequestAsync(_ctx.DungeonType, stage, ct);
			if (enter.ok == false)
			{
				return null;
			}

			DungeonContext next = new DungeonContext(_ctx.DungeonType, stage);
			next.RiftSkillId = _ctx.RiftSkillId;
			next.Run = enter.run;
			return next;
		}

		// 계정 누적 경험치. Account 는 순수 C# 싱글톤이고 Loadout 은 생성자에서 채워지므로 가드가 없다.
		private static int currentExp()
		{
			return Account.Instance.Loadout.Exp;
		}

		// ── 승리 처리 ─────────────────────────────────────────────────

		// 최고 클리어 단계 갱신 — 다음 단계 해금과 퀘스트 판정의 근거다. 서버도 정산에서 같은 값으로 저장한다.
		// 퀘스트(QuestTargetType.DungeonClear)가 최고 클리어 단계를 다시 보게 하는 지점이기도 하다.
		private void markVictory()
		{
			if (_ctx.DungeonType == EDT.Dungeon.Rift)
			{
				finishRift();
				return;
			}

			DungeonProgress.MarkStageCleared(_ctx.DungeonType, _ctx.Stage);
			EventManager.Instance.Publish(new DungeonStageClearedEvent(_ctx.DungeonType, _ctx.Stage));
		}

		// 종료 시점에 바닥에 남은 보상 드랍 — 미궁은 출구로 빠져나가며 두고 가므로 지우고, 나머지는 모두 획득한다.
		private void settleGroundDrops()
		{
			if (DropManager.HasInstance == false)
			{
				return;
			}

			if (_ctx.DungeonType == EDT.Dungeon.Labyrinth)
			{
				DropManager.Instance.DiscardAllRewardDrops();
				return;
			}

			DropManager.Instance.CollectAllRewardDrops();
		}

		// 종료 메시지 — 균열은 라이프를 다 잃으면 패배, 제한시간이 끝나면 시간 종료다(둘 다 보상은 정산된다).
		private void showEndMessage()
		{
			DungeonMessage message = UIManager.HasInstance ? UIManager.Instance.DungeonMessage : null;
			if (message == null)
			{
				return;
			}

			if (_ctx.DungeonType == EDT.Dungeon.Rift)
			{
				if (_timedOut == true)
				{
					message.ShowClear("시간 종료!");
				}
				else
				{
					message.ShowFailed("방어 실패!");
				}

				return;
			}

			message.ShowClear("던전 클리어!");
		}

		// 결과창 한 장 — 런 중 주운 보상(처치 드랍 + 상자 드랍) + 클리어 보상. 같은 종류는 결과창이 합쳐 보여준다.
		// 드랍은 주울 때 이미 지급됐고, 클리어 보상은 서버 응답(applyClearResponse) 또는 로컬 정산이 지급했다.
		private DungeonClearResponse buildResultResponse(DungeonClearResponse clear)
		{
			DungeonClearResponse shown = buildLocalResponse(DungeonRunLedger.Instance.PickedRewards);
			if (clear == null || clear.rewards == null || clear.rewards.Length == 0)
			{
				return shown;
			}

			List<GrantedRewardDto> merged = new List<GrantedRewardDto>(shown.rewards);
			merged.AddRange(clear.rewards);
			shown.rewards = merged.ToArray();
			return shown;
		}

		// 로컬 런 정산 — 서버 없이 클리어 보상만 지급한다(미로그인·개발용 이동). markVictory 를 이미 거쳤다.
		private DungeonClearResponse settleLocal()
		{
			switch (_ctx.DungeonType)
			{
				case EDT.Dungeon.Rift:
					return settleRift();
				case EDT.Dungeon.Labyrinth:
				{
					Table_LabyrinthDungeon.Row row = DungeonProgress.FindLabyrinthStage(_ctx.Stage);
					return grantLocalClear((row != null) ? row.ClearRewardGroupID : 0);
				}
				case EDT.Dungeon.Ruins:
				{
					Table_RuinsDungeon.Row row = DungeonProgress.FindRuinsStage(_ctx.Stage);
					return grantLocalClear((row != null) ? row.ClearRewardGroupID : 0);
				}
			}

			return null;
		}

		// 클리어 보상 그룹을 로컬로 굴려 지급한다 — 공용 지급 경로라 획득 로그(RewardAcquiredEvent)가 여기서 찍힌다.
		private DungeonClearResponse grantLocalClear(int groupId)
		{
			List<ProjectOne.Reward.GrantedReward> granted = new List<ProjectOne.Reward.GrantedReward>();
			ProjectOne.Reward.RewardGranter.Grant(groupId, ProjectOne.Reward.RewardContext.DungeonClear, granted);

			Debug.Log($"[DungeonDirector] {_ctx.DungeonType} 로컬 정산 — Stage {_ctx.Stage}, 클리어 보상 {granted.Count}건");
			return buildLocalResponse(granted);
		}

		// ── 균열 정산 ─────────────────────────────────────────────────
		//
		// 최고 기록 = 종료 웨이브의 전 웨이브(모드의 ClearedWave).
		// 보상 = 입장보상(1웨이브 ~ 시작 웨이브 합) + 시작 웨이브 다음부터 통과한 웨이브 보상 합. 재화 1종이다(DungeonRules.GetRiftRunReward).

		// 모드를 멈추고 통과 웨이브를 확정해 기록한다 — 서버 정산 요청의 clearedWave 가 이 값이다.
		private void finishRift()
		{
			RiftDungeonMode mode = _currentMode as RiftDungeonMode;
			if (mode != null)
			{
				// 제한시간 종료면 모드는 아직 돌고 있다 — 결과창 뒤에서 스폰이 이어지지 않게 멈춘다.
				mode.Stop();
			}

			int clearedWave = (mode != null) ? mode.ClearedWave : _ctx.Stage - 1;
			_riftClearedWave = clearedWave;

			if (clearedWave > 0)
			{
				DungeonProgress.MarkStageCleared(EDT.Dungeon.Rift, clearedWave);
			}

			EventManager.Instance.Publish(new DungeonStageClearedEvent(EDT.Dungeon.Rift, clearedWave));
		}

		// 로컬 런 전용 지급.
		private DungeonClearResponse settleRift()
		{
			int startWave = _ctx.Stage;
			int clearedWave = _riftClearedWave;
			int total = DungeonRules.GetRiftRunReward(startWave, clearedWave);

			EDT.Currency currency = DungeonProgress.GetRiftRewardCurrency();
			DungeonClearResponse resp = new DungeonClearResponse();

			if (total > 0 && currency != EDT.Currency.None)
			{
				// 공용 지급 경로를 탄다 — 획득 로그(RewardAcquiredEvent)가 여기서 찍힌다.
				List<ProjectOne.Reward.GrantedReward> applied = new List<ProjectOne.Reward.GrantedReward>(1);
				ProjectOne.Reward.GrantedReward reward = default(ProjectOne.Reward.GrantedReward);
				reward.type = RewardType.Currency;
				reward.currency = currency;
				reward.count = total;
				applied.Add(reward);
				ProjectOne.Reward.RewardGranter.ApplyAll(applied);

				GrantedRewardDto granted = new GrantedRewardDto();
				granted.rewardType = (int)RewardType.Currency;
				granted.itemId = (int)currency;
				granted.count = total;
				resp.rewards = new GrantedRewardDto[] { granted };
			}
			else
			{
				resp.rewards = new GrantedRewardDto[0];
			}

			Debug.Log($"[DungeonDirector] 균열 로컬 정산 — 시작 {startWave}, 통과 {clearedWave}, 보상 {total} ({currency})");
			return resp;
		}

		// 로컬 지급분을 결과창 응답으로 옮긴다.
		// 장비는 인스턴스로, 나머지는 dto 로 결과창에 싣는다 — 결과창이 두 경로를 따로 그린다.
		private DungeonClearResponse buildLocalResponse(List<ProjectOne.Reward.GrantedReward> shown)
		{
			List<GrantedRewardDto> dtos = new List<GrantedRewardDto>();
			for (int i = 0; i < shown.Count; i++)
			{
				ProjectOne.Reward.GrantedReward reward = shown[i];
				if (reward.equipment != null)
				{
					_grantedEquipments.Add(reward.equipment);
					continue;
				}

				GrantedRewardDto dto = new GrantedRewardDto();
				dto.rewardType = (int)reward.type;
				dto.itemId = (reward.type == RewardType.Currency) ? (int)reward.currency : reward.itemId;
				dto.count = reward.count;
				dtos.Add(dto);
			}

			DungeonClearResponse resp = new DungeonClearResponse();
			resp.rewards = dtos.ToArray();
			return resp;
		}

		// ── 서버 클리어 요청 ──────────────────────────────────────────

		private void beginClearRequestIfNeeded()
		{
			if (_clearRequestSent == true)
			{
				return;
			}

			_clearRequestSent = true;
			_clearTask = sendDungeonClearAsync(true);
		}

		private UniTask<DungeonClearResponse> sendDungeonClearAsync(bool cleared)
		{
			_clearTcs = new UniTaskCompletionSource<DungeonClearResponse>();
			NetworkManager.Instance.RequestDungeonClear(buildClearRequest(cleared), onClearResponse);
			return _clearTcs.Task;
		}

		private void onClearResponse(bool success, DungeonClearResponse data, string error)
		{
			if (success == false || data == null)
			{
				Debug.LogWarning("[DungeonDirector] 던전 클리어 서버 처리 실패: " + error);
				_clearTcs?.TrySetResult(null);
				return;
			}

			_clearTcs?.TrySetResult(data);
		}

		// 실패·강제 귀환 — 런을 닫고 연 상자를 저장한다. 응답은 기다리지 않는다(결과창이 없다).
		private void sendDungeonClearFailLog()
		{
			if (_ctx == null || DungeonRunLedger.Instance.IsServerRun == false || NetworkManager.Instance.IsLoggedIn == false)
			{
				return;
			}

			NetworkManager.Instance.RequestDungeonClear(buildClearRequest(false), null);
		}

		// 어느 런이 어떻게 끝났는지와 연 상자만 보낸다. 보상 계산은 서버가 RewardGroupID 와 런 시드로 한다.
		private DungeonClearRequest buildClearRequest(bool cleared)
		{
			DungeonClearRequest req = new DungeonClearRequest();
			req.runId = DungeonRunLedger.Instance.RunId;
			req.dungeonType = (int)_ctx.DungeonType;
			req.stage = _ctx.Stage;
			req.cleared = cleared;
			req.clearedWave = _riftClearedWave;
			req.chests = DungeonRunLedger.Instance.GetChests();

			// 서버가 클리어 경험치를 같은 마스터리에 적립한다(마스터리 설계 5.2). 미착용이면 0.
			Table_WeaponMastery.Row mastery = Account.Instance.Mastery.CurrentMastery;
			req.masteryId = (mastery != null) ? (int)mastery.ID : 0;
			return req;
		}

		// 서버 응답을 내 계정에 반영 — 경험치(권위) + 획득 아이템/재화.
		private void applyClearResponse(DungeonClearResponse resp)
		{
			if (resp == null)
			{
				return;
			}

			// 최고 단계 — 서버 정산값으로 맞춘다.
			DungeonProgress.ApplyEntry(resp.entry);

			// 캐릭터는 서버 권위값, 마스터리는 증가분만 적립된다 (마스터리 설계 5.2).
			// 아직 서버에 올라가지 않은 필드 처치 경험치를 더한다 — 빼면 그만큼 경험치가 줄어든다.
			Account.Instance.SetExpAuthoritative(resp.exp + FieldKillLedger.Instance.UnsettledExp);

			// 장비는 서버가 UID·등급·품질까지 확정해 인스턴스로 내려준다 — 그대로 넣는다.
			if (resp.equipments != null)
			{
				for (int i = 0; i < resp.equipments.Length; i++)
				{
					grantEquipmentFromServer(resp.equipments[i]);
				}
			}

			if (resp.rewards == null)
			{
				return;
			}

			DungeonRunState.Instance.AddRewards(resp.rewards);

			// rewards 에는 스택 아이템과 재화만 온다.
			for (int i = 0; i < resp.rewards.Length; i++)
			{
				GrantedRewardDto g = resp.rewards[i];
				switch ((RewardType)g.rewardType)
				{
				case RewardType.Item:
				case RewardType.ItemPool:
					if (g.itemId > 0 && g.count > 0)
					{
						Account.Instance.Inventory.Add(g.itemId, g.count);
					}

					break;
				case RewardType.Currency:
				{
					EDT.Currency type = (EDT.Currency)g.itemId;
					int current = Account.Instance.Wallet.GetAmount(type);
					Account.Instance.Wallet.SetAmount(type, current + g.count);
					break;
				}
				}
			}
		}

		// 서버가 만든 장비 인스턴스를 같은 UID 로 인벤토리에 넣는다 — 서버 USER_INVENTORY 와 UID 가 일치해야
		// 이후 장착 저장(SaveLoadout)의 보유 검증을 통과한다.
		private void grantEquipmentFromServer(EquipmentInstanceDto src)
		{
			EquipmentInstance instance = ProjectOne.Reward.RewardGranter.CreateServerEquipment(src);
			if (instance == null)
			{
				return;
			}

			Account.Instance.Inventory.AddEquipment(instance);
			_grantedEquipments.Add(instance);
		}

		// ── 정리 ──────────────────────────────────────────────────────

		// 던전 종료 시 유닛/스폰/풀/맵 일괄 정리.
		// 전투씬 수명 매니저는 씬과 함께 파괴되지만, 영속(DontDestroyOnLoad) 매니저는 명시적으로 비운다.
		private void cleanupAll()
		{
			if (UnitManager.HasInstance == true)
			{
				UnitManager.Instance.ClearAll();
			}

			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.Clear();
			}

			if (SummonManager.HasInstance == true)
			{
				SummonManager.Instance.ReleaseAll();
			}

			MonsterPoolHub.Instance.Clear();
			SummonPoolHub.Instance.Clear();

			// 런 원장 — 이후의 획득(마을·필드)은 결과창 합산에 넣지 않는다.
			DungeonRunLedger.Instance.End();

			if (DropManager.HasInstance == true)
			{
				DropManager.Instance.Clear();
			}

			if (UIManager.HasInstance == true)
			{
				UIManager.Instance.ReleaseWorldGauge();
				UIManager.Instance.ReleaseDungeonHud();
			}

			if (MapManager.HasInstance == true)
			{
				MapManager.Instance.UnloadMap();
			}

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
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
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
