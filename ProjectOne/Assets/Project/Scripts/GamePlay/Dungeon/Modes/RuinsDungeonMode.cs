using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Resources;
using ProjectOne.Reward;
using ProjectOne.UI;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 유적 던전 — 방어 시스템 무력화 후 보물 획득.
	//
	// 1) 전투방 — 움직이지 않는 코어(보스)와 방어장치가 있다. 코어의 HP 구간마다 방어 시스템이
	//    던전 전체에 공격을 순서대로 내리고, 구간 진입 시 가디언을 소환한다 (RuinsCoreSequencer).
	// 2) 코어를 파괴하면 남은 몬스터가 함께 사라지고 상자가 나타난다(맵에 보상방 문이 있으면 함께 열린다).
	// 3) 보상방 — 상자는 모두 단계의 ChestGrade 외형으로 생성되고, 보상 그룹(ChestRewardGroupIDs)은 입장마다
	//    무작위로 배정된다 — 겉모습만 보고 원하는 보상을 골라 먹을 수 없게 한다. 반경 안에 머물면 게이지가 차고, 다 차면 열려 그 상자의 보상 그룹을 바로 지급한다.
	//    한 판에 열 수 있는 상자 수는 보물 열쇠 개수다(소모가 아니라 카운트).
	// 4) 열쇠를 다 쓰거나 남은 상자가 없으면 포탈이 생기고, 포탈에 들어가면 클리어다.
	//
	// 몬스터에게 죽는 것은 StageModeBase 의 공통 Defeat 판정 → 디렉터의 부활 팝업 경로를 그대로 탄다.
	// 제한시간은 디렉터가 판정한다.
	public sealed class RuinsDungeonMode : StageModeBase
	{
		// 상자 개봉에 머물러야 하는 시간(초) — 미궁과 같다.
		private const float ChestOpenSeconds = 2f;

		// 등급별 상자 프리팹 주소 = 접두사 + DungeonChestGrade 이름 (Prefab_DungeonChest_Premium …)
		private const string ChestAddressPrefix = "Prefab_DungeonChest_";

		// 열쇠를 다 쓴 뒤 닫힌 상자 범위에 들어가면 띄우는 제한 메시지
		private const string NoKeyMessage = "열쇠가 부족하여 상자를 열수 없습니다";

		private Table_RuinsDungeon.Row _row;
		private RuinsMap _map;
		private int[] _groups;

		private UnitBase _core;
		private bool _coreDestroyed;
		private readonly RuinsCoreSequencer _sequencer = new RuinsCoreSequencer();

		// 이번 판에 생성한 상자와 상자별 보상 그룹 — 같은 인덱스끼리 짝이다.
		private readonly List<DungeonChest> _chests = new List<DungeonChest>(5);
		private readonly List<int> _chestRewardGroups = new List<int>(5);

		private DungeonChest _chestTarget;
		private float _chestProgress;
		private int _openedChests;
		private int _keyCount;
		private bool _portalShown;
		private bool _noKeyWarningShown;

		// 이번 판에 지급한 상자 보상. 결과창에 보여준다.
		private readonly List<GrantedReward> _granted = new List<GrantedReward>();

		public List<GrantedReward> GrantedRewards
		{
			get { return _granted; }
		}

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

			await setupChestsAsync(ct);
			_keyCount = DungeonProgress.GetRuinsKeyCount();
			_map.SetPortalVisible(false);
			publishChestChanged();

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

				float dt = Time.deltaTime;
				UnitBase hero = findAliveHero();

				// 사망 연출·부활 팝업 동안에는 방어 시스템도 상자도 멈춘다 — 살아나자마자 맞으면 안 된다.
				if (hero == null)
				{
					cancelChest();
					setNoKeyWarning(false);
					continue;
				}

				if (_coreDestroyed == false)
				{
					if (_core.IsDead == true)
					{
						onCoreDestroyed();
						continue;
					}

					_sequencer.Tick(dt);
					continue;
				}

				updateChest(hero, dt);

				if (_portalShown == true && _map.IsInPortal(hero.CachedPos) == true)
				{
					_result = DungeonResult.Cleared;
				}
			}
		}

		protected override void OnFinished()
		{
			cancelChest();
			setNoKeyWarning(false);
			_sequencer.Cancel();
			releaseChests();
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

		// 방어 시스템 무력화 — 남은 몬스터는 처치가 아닌 풀 반환으로 걷는다(경험치·킬카운트 없음).
		private void onCoreDestroyed()
		{
			_coreDestroyed = true;
			_sequencer.Cancel();

			// 방어 시스템이 무력화되면 제한시간은 더 흐르지 않는다 — 상자를 고르다 시간초과로 실패하지 않게 한다.
			if (DungeonDirector.HasInstance == true)
			{
				DungeonDirector.Instance.StopTimer();
			}

			MonsterSpawnManager.Instance.ClearAlive();
			_map.OpenDoor();
			revealChests();

			tryShowPortal();
		}

		// ── 상자 ──────────────────────────────────────────────────────

		// 상자 자리 앞에서부터 ChestCount 개에 ChestGrade 외형의 상자를 만들어 둔다.
		// 상자는 코어가 파괴될 때 나타난다 — 그 전에는 꺼 둔다(코어가 상자 자리를 지키고 있다).
		private async UniTask setupChestsAsync(CancellationToken ct)
		{
			_chests.Clear();
			_chestRewardGroups.Clear();

			int[] groups = _row.ChestRewardGroupIDs;
			IReadOnlyList<Transform> slots = _map.ChestSlots;
			int slotCount = (slots != null) ? slots.Count : 0;

			if (groups.Length != _row.ChestCount)
			{
				Debug.LogError($"[RuinsDungeonMode] ChestCount({_row.ChestCount}) 와 ChestRewardGroupIDs({groups.Length}) 개수가 다릅니다 — 짧은 쪽에 맞춥니다. RuinsDungeon:{_row.ID}");
			}

			int count = Mathf.Min(_row.ChestCount, groups.Length);
			if (slotCount < count)
			{
				Debug.LogError($"[RuinsDungeonMode] 맵의 상자 자리({slotCount})가 상자 수({count})보다 적습니다 — RuinsDungeon:{_row.ID}");
				count = slotCount;
			}

			string address = ChestAddressPrefix + _row.ChestGrade.ToString();
			for (int i = 0; i < count; i++)
			{
				GameObject go = await AddressableHelper.TryInstantiateAsync(address, _map.transform, true, ct);
				if (go == null)
				{
					continue;
				}

				DungeonChest chest = go.GetComponent<DungeonChest>();
				if (chest == null)
				{
					Debug.LogError($"[RuinsDungeonMode] {go.name} 에 DungeonChest 가 없습니다.");
					AddressableHelper.ReleaseInstance(go);
					continue;
				}

				go.transform.position = slots[i].position;
				go.SetActive(false);

				_chests.Add(chest);
				_chestRewardGroups.Add(groups[i]);
			}

			shuffleRewardGroups();
		}

		// 어느 상자에 무엇이 들었는지 모르게 섞는다 (Fisher-Yates).
		private void shuffleRewardGroups()
		{
			for (int i = _chestRewardGroups.Count - 1; i > 0; i--)
			{
				int j = Random.Range(0, i + 1);
				int temp = _chestRewardGroups[i];
				_chestRewardGroups[i] = _chestRewardGroups[j];
				_chestRewardGroups[j] = temp;
			}
		}

		private void revealChests()
		{
			for (int i = 0; i < _chests.Count; i++)
			{
				_chests[i].gameObject.SetActive(true);
			}
		}

		// 생성한 상자는 모드가 해제한다 — 맵 인스턴스와 핸들이 따로다.
		private void releaseChests()
		{
			for (int i = 0; i < _chests.Count; i++)
			{
				if (_chests[i] != null)
				{
					AddressableHelper.ReleaseInstance(_chests[i].gameObject);
				}
			}

			_chests.Clear();
			_chestRewardGroups.Clear();
		}

		private int getKeyRemaining()
		{
			int remaining = _keyCount - _openedChests;
			return (remaining > 0) ? remaining : 0;
		}

		private void updateChest(UnitBase hero, float dt)
		{
			DungeonChest nearest = findNearestChest(hero.CachedPos);

			// 열쇠를 다 쓰면 남은 상자는 열 수 없다 — 범위 안에 있는 동안만 이유를 알려 준다.
			if (getKeyRemaining() <= 0)
			{
				cancelChest();
				setNoKeyWarning(nearest != null);
				return;
			}

			setNoKeyWarning(false);

			if (nearest == null)
			{
				cancelChest();
				return;
			}

			// 대상이 바뀌면 처음부터 다시 채운다.
			if (nearest != _chestTarget)
			{
				cancelChest();
				_chestTarget = nearest;

				InteractionGauge gauge = getGauge();
				if (gauge != null)
				{
					gauge.Show(hero, InteractionKind.Chest);
				}
			}

			_chestProgress += dt;

			InteractionGauge current = getGauge();
			if (current != null)
			{
				current.SetProgress(_chestProgress / ChestOpenSeconds);
			}

			if (_chestProgress < ChestOpenSeconds)
			{
				return;
			}

			openChest(_chestTarget);
			cancelChest();
		}

		// TODO(STEP 14) — 지금은 로컬 지급이다. 서버 권위로 옮길 때 여기와 디렉터의 유적 정산을 함께 바꾼다.
		private void openChest(DungeonChest chest)
		{
			chest.Open();

			int index = _chests.IndexOf(chest);
			if (index >= 0 && index < _chestRewardGroups.Count)
			{
				// 공용 지급 경로 — 획득 로그(RewardAcquiredEvent)가 여기서 찍힌다.
				RewardGranter.Grant(_chestRewardGroups[index], RewardContext.DungeonClear, _granted);
			}

			_openedChests++;
			publishChestChanged();
			tryShowPortal();
		}

		private void publishChestChanged()
		{
			EventManager.Instance.Publish(new RuinsChestChangedEvent(_openedChests, _chests.Count, getKeyRemaining()));
		}

		// 코어가 파괴된 뒤, 열쇠를 다 썼거나 남은 상자가 없으면 포탈이 생긴다.
		private void tryShowPortal()
		{
			if (_portalShown == true || _coreDestroyed == false)
			{
				return;
			}

			if (getKeyRemaining() > 0 && _openedChests < _chests.Count)
			{
				return;
			}

			_portalShown = true;
			_map.SetPortalVisible(true);
		}

		// 상태가 바뀔 때만 호출한다 — 제한 메시지는 Hide 를 부를 때까지 떠 있으므로 매 프레임 부를 필요가 없다.
		private void setNoKeyWarning(bool show)
		{
			if (_noKeyWarningShown == show || UIManager.HasInstance == false)
			{
				return;
			}

			_noKeyWarningShown = show;

			if (show == true)
			{
				UIManager.Instance.ShowWarningMessage(NoKeyMessage);
			}
			else
			{
				UIManager.Instance.HideWarningMessage();
			}
		}

		private void cancelChest()
		{
			if (_chestTarget == null)
			{
				return;
			}

			_chestTarget = null;
			_chestProgress = 0f;

			InteractionGauge gauge = getGauge();
			if (gauge != null)
			{
				gauge.Hide();
			}
		}

		private DungeonChest findNearestChest(Vector2 pos)
		{
			DungeonChest best = null;
			float bestSqr = float.MaxValue;

			for (int i = 0; i < _chests.Count; i++)
			{
				DungeonChest chest = _chests[i];
				if (chest == null || chest.IsOpened == true)
				{
					continue;
				}

				float sqr = (chest.Position - pos).sqrMagnitude;
				if (sqr > chest.Radius * chest.Radius || sqr >= bestSqr)
				{
					continue;
				}

				best = chest;
				bestSqr = sqr;
			}

			return best;
		}

		private static InteractionGauge getGauge()
		{
			if (UIManager.HasInstance == false)
			{
				return null;
			}

			return UIManager.Instance.InteractionGauge;
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
