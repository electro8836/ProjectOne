using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.UI;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 미궁 던전 — 탈출형 기믹.
	//
	// 시작 지점(Entry)에서 출발해 도착 구역(Exit)의 기관장치를 작동하면 클리어다 — 모든 구간을 정리한 뒤
	// 구역 안에 ExitDeviceSeconds 동안 머물면 불이 멈추고 되돌아가는 길이 장막으로 막힌다.
	// 아래에서 불(LabyrinthFireWall)이 일정 속도로 올라오고, 닿으면 부활 없이 즉시 실패다.
	// 트리거는 순서대로 하나씩 발동한다 — 발동하면 스폰 그룹이 나오고 장막이 앞길을 막는다.
	// 그 구간 몬스터를 모두 처치해야 장막이 걷히고 다음 트리거가 발동할 수 있다.
	// 상자 반경 안에 머물면 게이지가 차고, 다 차면 열려 그 상자 등급의 보상 그룹(Normal/Advanced/Premium)을 런 시드로 굴려
	// 바닥에 떨군다(DungeonRunLedger). 주운 것만 런 종료 정산에서 서버가 지급한다 — 끝날 때 바닥에 남은 것은 사라진다.
	//
	// 몬스터에게 죽는 것은 StageModeBase 의 공통 Defeat 판정 → 디렉터의 부활 팝업 경로를 그대로 탄다.
	// 제한시간은 디렉터가 판정한다.
	public sealed class LabyrinthDungeonMode : StageModeBase
	{
		// 상자 개봉에 머물러야 하는 시간(초)
		private const float ChestOpenSeconds = 2f;

		// 출구 기관장치 작동에 머물러야 하는 시간(초)
		private const float ExitDeviceSeconds = 3f;

		private Table_LabyrinthDungeon.Row _row;
		private LabyrinthMap _map;
		private int[] _groups;

		// 다음에 발동할 트리거 인덱스, 몬스터가 남아 진행 중인 트리거 인덱스(없으면 -1)
		private int _nextTrigger;
		private int _activeTrigger = -1;

		// 진행도 슬라이더 기준 — 시작 지점이 0, 도착 구역 중심이 1 이다.
		private float _startY;
		private float _exitY;

		private float _fireWait;
		private bool _fireDeath;

		// 기관장치를 작동해 불이 멈췄는가
		private bool _fireStopped;

		// 출구 기관장치 작동 게이지 — 구역 안에 있는 동안만 찬다.
		private bool _exitInteracting;
		private float _exitProgress;

		private DungeonChest _chestTarget;
		private float _chestProgress;
		private int _openedChests;
		private int _totalChests;

		private float _playerProgress;
		private float _fireProgress;

		// 상자 1개의 추첨 결과 버퍼 — 바닥에 떨구면 다시 쓴다.
		private readonly List<GrantedReward> _chestRolled = new List<GrantedReward>(8);

		// 캐릭터 위치 진행도 0~1
		public float PlayerProgress
		{
			get { return _playerProgress; }
		}

		// 불 윗변 진행도 0~1. 시작 지점보다 아래면 0 이다.
		public float FireProgress
		{
			get { return _fireProgress; }
		}

		public int OpenedChests
		{
			get { return _openedChests; }
		}

		public int TotalChests
		{
			get { return _totalChests; }
		}

		// 실패 원인이 불인지 — 디렉터가 실패 팝업 문구를 고른다.
		public bool IsFireDeath
		{
			get { return _fireDeath; }
		}

		protected override async UniTask RunAsync(DungeonContext ctx, CancellationToken ct)
		{
			_row = DungeonProgress.FindLabyrinthStage(ctx.Stage);
			if (_row == null)
			{
				Debug.LogError($"[LabyrinthDungeonMode] LabyrinthDungeon 행이 없습니다 — Stage {ctx.Stage}");
				return;
			}

			_map = findMap();
			if (_map == null)
			{
				Debug.LogError("[LabyrinthDungeonMode] 맵에 LabyrinthMap 이 없습니다 — 맵 프리팹 루트에 붙여야 합니다.");
				return;
			}

			if (_map.FireWall == null)
			{
				Debug.LogError("[LabyrinthDungeonMode] LabyrinthMap 에 FireWall 이 연결되지 않았습니다.");
				return;
			}

			_groups = GetSpawnGroups(_row.MonsterSpawnGroupIDs);
			if (_groups.Length != _map.Triggers.Count)
			{
				Debug.LogWarning($"[LabyrinthDungeonMode] 스폰 그룹 수({_groups.Length})와 트리거 수({_map.Triggers.Count})가 다릅니다 — LabyrinthDungeon:{_row.ID}");
			}

			// 팝업은 테이블 값을, 판정은 실제 배치를 쓴다 — 어긋나면 표시와 결과가 다르다.
			_totalChests = _map.Chests.Count;
			if (_totalChests != _row.ChestCount)
			{
				Debug.LogError($"[LabyrinthDungeonMode] 맵의 상자 수({_totalChests})와 ChestCount({_row.ChestCount})가 다릅니다 — LabyrinthDungeon:{_row.ID}");
			}

			_startY = _map.EntryPosition.y;
			_exitY = _map.ExitY;
			_fireWait = _row.FireDelay;

			// 재도전은 씬을 유지한 채 맵만 바꾸므로 히어로가 이전 판 위치에 남아 있다 — 매번 시작 지점으로 옮긴다.
			placeHeroAtEntry();
			_map.SetExitCurtain(false);

			EventManager.Instance.Publish(new LabyrinthChestChangedEvent(_openedChests, _totalChests));

			while (_result == DungeonResult.InProgress)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, ct);

				float dt = Time.deltaTime;
				UnitBase hero = findAliveHero();

				// 사망 연출·부활 팝업 동안에는 불도 상자도 멈춘다 — 살아나자마자 불에 잡히면 안 된다.
				if (hero == null)
				{
					cancelChest();
					cancelExit();
					continue;
				}

				updateFire(hero, dt);
				if (_result != DungeonResult.InProgress)
				{
					break;
				}

				updateTriggers(hero);

				_playerProgress = Mathf.InverseLerp(_startY, _exitY, hero.CachedPos.y);

				// 모든 구간을 정리해야 기관장치가 작동한다 — 마지막 구간은 막을 통로가 없어 장막 대신 이 조건이 막는다.
				// 출구 구역 안에서는 기관장치가 게이지를 쓴다 — 상자 게이지와 겹치지 않게 상자는 쉰다.
				if (isAllSegmentsCleared() == true && _map.IsInExit(hero.CachedPos) == true)
				{
					cancelChest();
					updateExit(hero, dt);
				}
				else
				{
					cancelExit();
					updateChest(hero, dt);
				}
			}
		}

		protected override void OnFinished()
		{
			cancelChest();
			cancelExit();
		}

		// ── 불 ────────────────────────────────────────────────────────

		private void updateFire(UnitBase hero, float dt)
		{
			// 기관장치를 작동하면 불은 더 오르지 않는다.
			if (_fireStopped == true)
			{
				return;
			}

			LabyrinthFireWall fire = _map.FireWall;

			if (_fireWait > 0f)
			{
				_fireWait -= dt;
			}
			else
			{
				fire.Advance(_row.FireSpeed * dt);
			}

			_fireProgress = Mathf.InverseLerp(_startY, _exitY, fire.TopY);

			if (fire.Overlaps(hero.CachedPos, hero.CachedRadius) == false)
			{
				return;
			}

			// 결과를 먼저 확정한다 — 히어로가 먼저 죽으면 공통 Defeat(부활 팝업)로 새어 나간다.
			_fireDeath = true;
			_result = DungeonResult.Failed;
			cancelChest();
			hero.ForceDie();
		}

		// ── 트리거 ────────────────────────────────────────────────────

		// 트리거 작동 → 몬스터 생성 → 장막 생성 → 몬스터 모두 처치 → 장막 해제 → 다음 트리거.
		private void updateTriggers(UnitBase hero)
		{
			IReadOnlyList<LabyrinthSpawnTrigger> triggers = _map.Triggers;

			// 진행 중인 구간 — 몬스터가 다 사라져야 장막이 걷힌다.
			// ActiveCount 는 스폰 요청 즉시 오르므로 소환 직후 곧바로 참이 되지 않는다.
			if (_activeTrigger >= 0)
			{
				if (AreMonstersCleared() == false)
				{
					return;
				}

				triggers[_activeTrigger].SetCurtain(false);
				_activeTrigger = -1;
			}

			if (_nextTrigger >= triggers.Count || triggers[_nextTrigger].Contains(hero.CachedPos) == false)
			{
				return;
			}

			LabyrinthSpawnTrigger trigger = triggers[_nextTrigger];
			_activeTrigger = _nextTrigger;
			_nextTrigger++;

			int index = trigger.Order - 1;
			if (index < 0 || index >= _groups.Length)
			{
				Debug.LogError($"[LabyrinthDungeonMode] 트리거 순서 {trigger.Order} 에 해당하는 스폰 그룹이 없습니다 — LabyrinthDungeon:{_row.ID}");
				return;
			}

			SpawnGroupRunner.SpawnGroupAt(_groups[index], _row.MonsterLevel, trigger.Slots);
			trigger.SetCurtain(true);
		}

		private bool isAllSegmentsCleared()
		{
			return _nextTrigger >= _map.Triggers.Count && _activeTrigger < 0;
		}

		// ── 출구 기관장치 ─────────────────────────────────────────────

		// 구역 안에 머무는 동안 게이지가 차고, 다 차면 불을 멈추고 되돌아가는 길을 막은 뒤 클리어한다.
		private void updateExit(UnitBase hero, float dt)
		{
			if (_exitInteracting == false)
			{
				_exitInteracting = true;
				_exitProgress = 0f;

				InteractionGauge gauge = getGauge();
				if (gauge != null)
				{
					gauge.Show(hero, InteractionKind.Device);
				}
			}

			_exitProgress += dt;

			InteractionGauge current = getGauge();
			if (current != null)
			{
				current.SetProgress(_exitProgress / ExitDeviceSeconds);
			}

			if (_exitProgress < ExitDeviceSeconds)
			{
				return;
			}

			cancelExit();
			_fireStopped = true;
			_map.SetExitCurtain(true);
			_playerProgress = 1f;
			_result = DungeonResult.Cleared;
		}

		private void cancelExit()
		{
			if (_exitInteracting == false)
			{
				return;
			}

			_exitInteracting = false;
			_exitProgress = 0f;

			InteractionGauge gauge = getGauge();
			if (gauge != null)
			{
				gauge.Hide();
			}
		}

		// ── 상자 ──────────────────────────────────────────────────────

		private void updateChest(UnitBase hero, float dt)
		{
			DungeonChest nearest = findNearestChest(hero.CachedPos);
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

		// 상자 인덱스는 맵 배치 순서다 — 서버가 상자별 시드를 같은 인덱스로 재현한다.
		private void openChest(DungeonChest chest)
		{
			chest.Open();

			// 보상은 상자 주변 바닥에 떨군다 — 주울 때 지급된다(획득 로그도 그때 찍힌다).
			int index = indexOfChest(chest);
			if (index >= 0
				&& DungeonRunLedger.Instance.OpenChest(index, chest.Grade, getRewardGroup(chest), _chestRolled) == true
				&& DropManager.HasInstance == true)
			{
				DropManager.Instance.SpawnChestDrops(chest.Position, _chestRolled, index);
			}

			_openedChests++;
			EventManager.Instance.Publish(new LabyrinthChestChangedEvent(_openedChests, _totalChests));
		}

		// 상자 외형 등급 → 이 단계의 등급별 보상 그룹(서버와 같은 규칙). 등급이 없는 상자(베이스 프리팹)는 일반으로 본다.
		private int getRewardGroup(DungeonChest chest)
		{
			if (chest.Grade == DungeonChestGrade.None)
			{
				Debug.LogError($"[LabyrinthDungeonMode] {chest.name} 의 등급이 없습니다 — 일반 보상으로 지급합니다. 등급 변형 프리팹(Prefab_DungeonChest_*)을 배치하세요.");
			}

			return DungeonRules.GetLabyrinthChestGroup(_row, chest.Grade);
		}

		private int indexOfChest(DungeonChest chest)
		{
			IReadOnlyList<DungeonChest> chests = _map.Chests;
			for (int i = 0; i < chests.Count; i++)
			{
				if (chests[i] == chest)
				{
					return i;
				}
			}

			return -1;
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

			IReadOnlyList<DungeonChest> chests = _map.Chests;
			for (int i = 0; i < chests.Count; i++)
			{
				DungeonChest chest = chests[i];
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

		private static LabyrinthMap findMap()
		{
			if (MapManager.HasInstance == false || MapManager.Instance.Current == null)
			{
				return null;
			}

			return MapManager.Instance.Current.GetComponent<LabyrinthMap>();
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
