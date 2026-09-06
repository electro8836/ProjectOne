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

		// 클리어 배너가 화면에 머무는 총 시간(초) — 결과창은 이 뒤에 뜬다.
		// **GoldDungeonUI 의 _titleHoldSeconds + _titleFadeSeconds 와 같은 값이어야 한다.**
		// 인스펙터에서 연출 시간을 바꾸면 여기도 같이 바꾼다.
		private const float ClearBannerSeconds = 2.5f;

		private Table_Dungeon.Row _dungeon;
		private Table_DungeonStage.Row _stage;
		private DungeonContext _ctx;

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

		// 플로우필드 재베이크 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		private static DungeonDirector _instance;

		public static bool HasInstance => _instance != null;

		public static DungeonDirector Instance => _instance;

		// 남은 제한시간(초). 무제한 단계면 0 이다.
		public float RemainTime => _remainTime;

		// 진행 중인 던전 종류. 아직 Begin 전이면 None 이다.
		public EDT.Dungeon DungeonType => (_ctx != null) ? _ctx.DungeonType : EDT.Dungeon.None;

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

			Table_DungeonStage.Row stage = ctx.FindStageRow();
			if (stage == null)
			{
				Debug.LogError($"[DungeonDirector] DungeonStage 없음 — {ctx.DungeonType} Stage {ctx.Stage}");
				return;
			}

			_dungeon = dungeon;
			_stage = stage;
			_ctx = ctx;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

			DungeonRunState.Instance.Reset();

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
			if (_hasTimeLimit == false || _timedOut == true || _ending == true || _timerPaused == true)
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

				// 제한시간 초과는 부활로 되돌릴 수 있는 상태가 아니다 — 왜 끝났는지만 알리고 마을로 보낸다.
				if (result == DungeonResult.Failed || _timedOut == true)
				{
					await showContinueAsync(ContinuePopupData.ForTimeout(), ct);
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

		private void startStage()
		{
			// 결과창의 "이번 판 획득 경험치"는 이 스냅샷과의 차이다 — 누적 집계 경로가 따로 없다.
			// Loadout.Exp 는 늘기만 하고 줄지 않는다(ApplyLevelup 은 아직 호출부가 없다).
			// 레벨업이 붙어 경험치를 되돌리게 되면 이 뺄셈을 다시 봐야 한다.
			_expAtStageStart = currentExp();

			_hasTimeLimit = _stage.TimeLimit > 0;
			_remainTime = _hasTimeLimit ? _stage.TimeLimit : 0f;
			_timedOut = false;
			_timerPaused = false;

			_currentMode = StageModeFactory.Create(_ctx.DungeonType);
			if (_currentMode == null)
			{
				return;
			}

			_currentMode.SetupAsync(_stage, _cts.Token).Forget();
		}

		private async UniTask loadMapAsync(CancellationToken ct)
		{
			// 던전은 단계마다 그리드맵 하나만 쓴다. 다음 단계로 넘어가면 여기서 교체된다.
			await MapManager.Instance.LoadMapAsync(_stage.MapID, ct);
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

			if (victory == true && _forcedLobbyReturn == false)
			{
				// 이전 판(다음 단계 재진입)의 지급 장비가 결과창에 섞이지 않게 비운다.
				_grantedEquipments.Clear();

				// 최고 클리어 단계 갱신 — 다음 단계 해금과 퀘스트 판정의 근거다.
				DungeonProgress.MarkStageCleared(_ctx.DungeonType, _ctx.Stage);

				// 퀘스트(QuestTargetType.DungeonClear)가 최고 클리어 단계를 다시 보게 하는 지점이다.
				EventManager.Instance.Publish(new DungeonStageClearedEvent(_ctx.DungeonType, _ctx.Stage));

				beginClearRequestIfNeeded();

				// 배너를 먼저 걸어 두고 응답을 기다린다 — 두 시계가 같이 흘러야 한다.
				// 응답이 빨라도 배너가 끝날 때까지, 배너가 끝나도 응답이 올 때까지 기다린다.
				// 이미 지난 배너를 await 하면 즉시 통과하므로 분기가 필요 없다.
				UniTask banner = UniTask.Delay(System.TimeSpan.FromSeconds(ClearBannerSeconds), cancellationToken: _cts.Token);

				DungeonClearResponse resp = await _clearTask;
				await banner;

				applyClearResponse(resp);

				// TODO(임시) — 결과창 확인용 더미 보상. 지울 때 아래 "임시 테스트" 영역과 이 줄을 함께 지운다.
				resp = TEMP_BuildDummyReward(resp);

				await showDungeonResultAsync(resp, _cts.Token);
			}
			else
			{
				sendDungeonClearFailLog();
			}

			cleanupAll();
			await GameFlow.Instance.ChangeStateAsync(new TownState());
		}

		// 결과창 — 서버 확정 보상을 보여주고, 다음 단계가 있으면 도전 여부를 묻는다.
		private async UniTask showDungeonResultAsync(DungeonClearResponse resp, CancellationToken ct)
		{
			DungeonResultUI ui = await UIManager.Instance.OpenWindowAsync<DungeonResultUI>(DungeonResultAddress, ct);
			if (ui == null)
			{
				return;
			}

			IReadOnlyList<GrantedRewardDto> rewards = (resp != null) ? resp.rewards : null;

			// 입장 횟수는 결과창이 직접 읽는다 — 여기서는 다음 단계가 존재하는지만 알려준다.
			bool hasNext = DungeonProgress.HasNextStage(_ctx.DungeonType, _ctx.Stage);

			int gainedExp = currentExp() - _expAtStageStart;
			if (gainedExp < 0)
			{
				gainedExp = 0;
			}

			DungeonResultAction action = await ui.WaitAsync(rewards, _grantedEquipments,
				_ctx.DungeonType, _ctx.Stage, gainedExp, hasNext, ct);

			// 마을 복귀는 던전 로딩을 띄우지 않는다 — 마을행에 던전 로딩을 거는 건 어색하다.
			// 다만 창은 반드시 닫는다. cleanupAll 은 윈도우를 걷지 않아서 그대로 두면 마을까지 따라간다.
			if (action == DungeonResultAction.ReturnTown)
			{
				await UIManager.Instance.CloseWindowAsync(false);
				return;
			}

			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToDungeon, ct);
			await UIManager.Instance.CloseWindowAsync(false);

			// 재도전도 다음 단계도 새 입장이다 — 횟수를 못 쓰면 그대로 마을로 나간다.
			if (DungeonProgress.TryConsumeEnter(_ctx.DungeonType) == false)
			{
				return;
			}

			int stage = (action == DungeonResultAction.NextStage) ? _ctx.Stage + 1 : _ctx.Stage;

			// 씬은 그대로 두고 맵/모드만 교체한다.
			await enterStageAsync(stage, ct);
		}

		// 계정 누적 경험치. Account 는 순수 C# 싱글톤이고 Loadout 은 생성자에서 채워지므로 가드가 없다.
		private static int currentExp()
		{
			return Account.Instance.Loadout.Exp;
		}

		// 지정한 단계로 재진입 — 씬 전환 없이 맵/모드만 새로 세운다.
		// 재도전(같은 단계)과 다음 단계가 같은 경로를 탄다.
		private async UniTask enterStageAsync(int stage, CancellationToken ct)
		{
			DungeonContext next = new DungeonContext(_ctx.DungeonType, stage);
			Table_DungeonStage.Row nextStage = next.FindStageRow();
			if (nextStage == null)
			{
				Debug.LogError($"[DungeonDirector] 재진입할 단계가 없습니다 — {_ctx.DungeonType} Stage {stage}");
				return;
			}

			// 이전 단계의 잔존물 정리 (히어로는 유지)
			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.ClearAlive();
			}

			_ctx = next;
			_stage = nextStage;
			_ending = false;
			_clearRequestSent = false;
			_forcedLobbyReturn = false;
			DungeonRunState.Instance.Reset();

			await loadMapAsync(ct);
			healAllHeroes();
			startStage();

			await LoadingManager.Instance.HideAsync();
			runGuardedAsync(ct).Forget();
		}

		// ── 임시 테스트 ───────────────────────────────────────────────
		//
		// TODO(임시) — 서버 미연동이라 결과창이 텅 비어 보인다. 눈으로 확인하려고 채우는 더미다.
		// **패킷 작업 시 이 영역 전체와 endDungeonAsync 의 호출 한 줄을 통째로 지운다.**
		//
		// applyClearResponse 뒤에서 부르므로 계정에는 아무것도 지급되지 않는다 — 화면에만 채운다.

		// 더미 골드 범위
		private const int TempDummyGoldMin = 1200;
		private const int TempDummyGoldMax = 8500;

		// 더미 장비 개수 범위 — 등급 색상이 섞여 보이도록 여러 개 만든다.
		private const int TempDummyEquipMin = 3;
		private const int TempDummyEquipMax = 6;

		// 더미 스택 아이템(소모품) 종류 수 범위
		private const int TempDummyItemMin = 1;
		private const int TempDummyItemMax = 3;

		// 더미 획득 경험치
		private const int TempDummyExp = 350;

		private DungeonClearResponse TEMP_BuildDummyReward(DungeonClearResponse actual)
		{
			// 서버가 실제로 응답했다면 그대로 쓴다.
			if (actual != null && actual.rewards != null && actual.rewards.Length > 0)
			{
				return actual;
			}

			// 경험치는 resp 가 아니라 DungeonStage.RewardExp 를 읽어 표시된다.
			// 테이블이 비어 있어 "+0" 으로 뜨므로 메모리 값만 덮어쓴다(바이트 파일은 그대로).
			Table_DungeonStage.Row stageRow = _stage;
			if (stageRow != null && stageRow.RewardExp <= 0)
			{
				stageRow.RewardExp = TempDummyExp;
			}

			List<GrantedRewardDto> list = new List<GrantedRewardDto>();

			GrantedRewardDto gold = new GrantedRewardDto();
			gold.rewardType = (int)RewardType.Currency;
			gold.itemId = (int)EDT.Currency.Gold;
			gold.count = Random.Range(TempDummyGoldMin, TempDummyGoldMax);
			list.Add(gold);

			// 장비와 스택 아이템을 갈라 담는다 — 표시 경로가 다르다.
			List<int> equipIds = new List<int>();
			List<int> stackIds = new List<int>();
			Dictionary<int, Table_Item.Row>.Enumerator e = Table_Item.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				int id = e.Current.Key;
				if (Table_Equipment.Get(id) != null)
				{
					equipIds.Add(id);
				}
				else
				{
					stackIds.Add(id);
				}
			}

			// 장비 — 등급을 섞어 인스턴스로 만든다. 품질·순도는 팩토리가 굴린다.
			// dto 로 싣지 않는 이유: GrantedRewardDto 에 등급·품질이 없어 결과창이 채울 수 없다.
			int equipCount = Random.Range(TempDummyEquipMin, TempDummyEquipMax + 1);
			for (int i = 0; i < equipCount && equipIds.Count > 0; i++)
			{
				int index = Random.Range(0, equipIds.Count);
				int itemId = equipIds[index];
				equipIds.RemoveAt(index);

				ItemGradeType grade = (ItemGradeType)Random.Range((int)ItemGradeType.Normal, (int)ItemGradeType.Mythic + 1);
				EquipmentInstance instance = EquipmentFactory.CreateFixed(itemId, grade);
				if (instance != null)
				{
					_grantedEquipments.Add(instance);
				}
			}

			// 스택 아이템 — 중복을 빼야 합산으로 한 칸이 되지 않고 칸 수가 눈에 보인다.
			int stackCount = Random.Range(TempDummyItemMin, TempDummyItemMax + 1);
			for (int i = 0; i < stackCount && stackIds.Count > 0; i++)
			{
				int index = Random.Range(0, stackIds.Count);

				GrantedRewardDto item = new GrantedRewardDto();
				item.rewardType = (int)RewardType.Item;
				item.itemId = stackIds[index];
				item.count = Random.Range(1, 4);
				list.Add(item);

				stackIds.RemoveAt(index);
			}

			DungeonClearResponse dummy = new DungeonClearResponse();
			dummy.exp = (actual != null) ? actual.exp : 0;
			dummy.rewards = list.ToArray();

			Debug.Log($"[DungeonDirector] TODO(임시) 더미 보상 — 골드 {gold.count}, 장비 {_grantedEquipments.Count}개, 스택 {list.Count - 1}종");
			return dummy;
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

		private void sendDungeonClearFailLog()
		{
			if (_ctx == null || NetworkManager.Instance.IsLoggedIn == false)
			{
				return;
			}

			NetworkManager.Instance.RequestDungeonClear(buildClearRequest(false), null);
		}

		// 어느 던전의 몇 단계를 클리어했는지만 보낸다. 보상 계산은 서버가 RewardGroupID 로 한다.
		private DungeonClearRequest buildClearRequest(bool cleared)
		{
			DungeonClearRequest req = new DungeonClearRequest();
			req.dungeonType = (int)_ctx.DungeonType;
			req.stage = _ctx.Stage;
			req.cleared = cleared;
			return req;
		}

		// 서버 응답을 내 계정에 반영 — 경험치(권위) + 획득 아이템/재화.
		private void applyClearResponse(DungeonClearResponse resp)
		{
			if (resp == null)
			{
				return;
			}

			// 캐릭터는 서버 권위값, 마스터리는 증가분만 적립된다 (마스터리 설계 5.2).
			Account.Instance.SetExpAuthoritative(resp.exp);

			if (resp.rewards == null)
			{
				return;
			}

			DungeonRunState.Instance.AddRewards(resp.rewards);

			for (int i = 0; i < resp.rewards.Length; i++)
			{
				GrantedRewardDto g = resp.rewards[i];
				switch ((RewardType)g.rewardType)
				{
				case RewardType.Item:
				case RewardType.ItemPool:
					grantItemFromServer(g.itemId, g.count);
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

		// 서버가 준 아이템을 인벤토리에 넣는다.
		//
		// 장비는 스택이 아니라 인스턴스 단위이므로 Inventory.Add 로 넣으면 안 된다 (아이템 설계 4장).
		// STEP 6 에서 장비가 UID 단위가 되면서 생긴 불일치를 여기서 바로잡는다.
		//
		// **한계 — 서버가 등급을 내려주지 못한다.** GrantedRewardDto 에 등급·순도·품질·uid 가 없어
		// 클라가 Item.Grade 기준으로 인스턴스를 만든다. 이건 설계가 금지한 "등급 자동 폴백"이 아니라
		// **DTO 스키마의 한계**다. DTO 확장은 STEP 14 에서 하며, 그때 이 분기를 제거한다.
		private void grantItemFromServer(int itemId, int count)
		{
			if (itemId <= 0 || count <= 0)
			{
				return;
			}

			if (Table_Equipment.Get(itemId) == null)
			{
				Account.Instance.Inventory.Add(itemId, count);
				return;
			}

			Table_Item.Row item = Table_Item.Get(itemId);
			ItemGradeType grade = (item != null && item.Grade != ItemGradeType.None) ? item.Grade : ItemGradeType.Normal;

			for (int i = 0; i < count; i++)
			{
				EquipmentInstance instance = EquipmentFactory.CreateFixed(itemId, grade);
				if (instance != null)
				{
					Account.Instance.Inventory.AddEquipment(instance);
					_grantedEquipments.Add(instance);
				}
			}
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
