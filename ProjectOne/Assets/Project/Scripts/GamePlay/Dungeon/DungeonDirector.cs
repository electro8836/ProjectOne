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
		private const string DungeonResultAddress = "Prefab_DungeonResult";
		private const string DungeonContinueAddress = "Prefab_DungeonContinue";

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

		// 플로우필드 재베이크 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		private static DungeonDirector _instance;

		public static bool HasInstance => _instance != null;

		public static DungeonDirector Instance => _instance;

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

			await loadMapAsync(_cts.Token);
			await spawnHeroAsync(_cts.Token);

			// 월드 오브젝트 풀은 첫 처치 전에 준비돼 있어야 한다 — 없으면 보상 드랍이 유실된다.
			await DropManager.Instance.PrepareAsync(_cts.Token);

			// 던전 입장은 항상 완전한 상태에서 시작한다 (기반테이블 5.3)
			healAllHeroes();

			startStage();

			// 진행 감시는 백그라운드로 — 여기서 await 를 끝내야 로딩 화면이 걷힌다.
			runGuardedAsync(_cts.Token).Forget();
		}

		// DungeonHUD 나가기 버튼 → 즉시 종료(보상 없음)
		public void RequestExit()
		{
			_forcedLobbyReturn = true;
			endDungeonAsync(false).Forget();
		}

		private void Update()
		{
			updateFlowFieldBake();
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

		// 부활을 선택하면 true. 귀환이거나 부활 불가면 false.
		private async UniTask<bool> handleDefeatAsync(CancellationToken ct)
		{
			int used = DungeonRunState.Instance.ReviveUsedCount;
			int max = _dungeon.MaxRevivalCount;

			// MaxRevivalCount 가 None(0)이면 부활 불가 던전이다 (맵 설계 6절).
			if (max <= 0 || used >= max)
			{
				return false;
			}

			// n회차 부활 비용 = RevivalCost + RevivalCostStep × (n - 1)
			int cost = _dungeon.RevivalCost + _dungeon.RevivalCostStep * used;

			Time.timeScale = 0f;
			bool revive = false;
			DungeonContinueUI ui = await UIManager.Instance.OpenWindowAsync<DungeonContinueUI>(DungeonContinueAddress, ct);
			if (ui != null)
			{
				revive = await ui.WaitChoiceAsync(_dungeon.RevivalCostType, cost, max - used, max, ct);
				await UIManager.Instance.CloseWindowAsync(false);
			}

			Time.timeScale = 1f;

			if (revive == false)
			{
				_forcedLobbyReturn = true;
				return false;
			}

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
				// 최고 클리어 단계 갱신 — 다음 단계 해금과 퀘스트 판정의 근거다.
				DungeonProgress.MarkStageCleared(_ctx.DungeonType, _ctx.Stage);

				// 퀘스트(QuestTargetType.DungeonClear)가 최고 클리어 단계를 다시 보게 하는 지점이다.
				EventManager.Instance.Publish(new DungeonStageClearedEvent(_ctx.DungeonType, _ctx.Stage));

				beginClearRequestIfNeeded();
				DungeonClearResponse resp = await _clearTask;
				applyClearResponse(resp);

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

			// 다음 단계가 존재하고 입장 횟수가 남아 있을 때만 도전 버튼을 켠다.
			bool hasNext = DungeonProgress.HasNextStage(_ctx.DungeonType, _ctx.Stage);
			bool canChallenge = hasNext && DungeonProgress.CanEnter(_ctx.DungeonType);

			bool challengeNext = await ui.WaitAsync(rewards, _ctx.DungeonType, _ctx.Stage, canChallenge, ct);

			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToDungeon, ct);
			await UIManager.Instance.CloseWindowAsync(false);

			if (challengeNext == true && DungeonProgress.TryConsumeEnter(_ctx.DungeonType) == true)
			{
				// 다음 단계는 새 입장이다 — 씬은 그대로 두고 맵만 교체한다.
				await enterNextStageAsync(ct);
			}
		}

		// 다음 단계로 재진입 — 씬 전환 없이 맵/모드만 새로 세운다.
		private async UniTask enterNextStageAsync(CancellationToken ct)
		{
			DungeonContext next = new DungeonContext(_ctx.DungeonType, _ctx.Stage + 1);
			Table_DungeonStage.Row nextStage = next.FindStageRow();
			if (nextStage == null)
			{
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
		private static void grantItemFromServer(int itemId, int count)
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
