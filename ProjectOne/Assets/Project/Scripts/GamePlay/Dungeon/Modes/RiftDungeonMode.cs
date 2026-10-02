using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Shared;
using ProjectOne.Skill;
using ProjectOne.Unit;
using ProjectOne.Unit.AI;

namespace ProjectOne.Dungeon
{
	// 균열 던전 — 경로 방어형 무한 웨이브.
	//
	// 웨이브마다 RiftDungeon 행(Wave)의 그룹을 1초 간격으로 한 마리씩 시작지점(스폰 슬롯)에 내보낸다.
	// 몬스터는 공격하지 않고 웨이포인트를 따라 도착지점으로 가며, 도착하면 사라지고 라이프가 1 깎인다.
	// 그 웨이브를 다 내보냈고 남은 몬스터가 0이 되면(처치든 도착이든) 곧바로 다음 웨이브다 — 끝이 없다.
	//
	// 라이프가 0이 되면 _result = Cleared 로 한 판을 끝낸다. 균열에서 Cleared 는 "정산 종료"다.
	// 제한시간 초과는 DungeonDirector 가 판정하고 Stop 으로 멈춘다.
	public sealed class RiftDungeonMode : StageModeBase
	{
		private const int MaxLife = 5;

		// 한 마리씩 내보내는 간격(초)
		private const float SpawnInterval = DungeonRules.RiftSpawnIntervalSeconds;

		// 처치 시 쌓이는 스킬 게이지. 엘리트·보스는 같은 값이다.
		private const int NormalGauge = 25;
		private const int EliteGauge = 200;

		private readonly List<Vector3> _path = new List<Vector3>();

		// 도착 콜백은 유닛 틱 도중에 불린다 — 거기서 풀 반환하면 순회 중인 목록이 바뀌므로 모아 뒀다가 루프에서 처리한다.
		private readonly List<Monster> _arrived = new List<Monster>();

		private Table_RiftSkill.Row _skill;
		private int _riftSkillId;
		private int _gauge;
		private int _life;

		// 끝까지 넘긴 마지막 웨이브. 시작 웨이브를 못 넘기면 시작 웨이브 - 1 이다.
		private int _clearedWave;

		private bool _stopped;

		private Action<MonsterKillEvent> _onMonsterKill;
		private Action<RiftSkillUseRequestedEvent> _onSkillUseRequested;
		private Action<Monster> _onMonsterArrived;

		// 정산용 — 최고 기록 후보(종료 웨이브의 전 웨이브)
		public int ClearedWave
		{
			get { return _clearedWave; }
		}

		// 정산 시점에 진행을 멈춘다. 이후 스폰·도착·게이지를 더 세지 않는다.
		public void Stop()
		{
			_stopped = true;
		}

		protected override async UniTask RunAsync(DungeonContext ctx, CancellationToken ct)
		{
			int wave = ctx.Stage;
			_clearedWave = wave - 1;
			_life = MaxLife;
			_gauge = 0;

			if (collectPath() == false)
			{
				Debug.LogError("[RiftDungeonMode] 맵에 DungeonWaypoint 가 없습니다 — 몬스터가 갈 곳이 없습니다.");
				return;
			}

			setupSkill(ctx.RiftSkillId);

			_onMonsterKill = onMonsterKill;
			_onSkillUseRequested = onSkillUseRequested;
			_onMonsterArrived = onMonsterArrived;
			EventManager.Instance.Subscribe<MonsterKillEvent>(_onMonsterKill);
			EventManager.Instance.Subscribe<RiftSkillUseRequestedEvent>(_onSkillUseRequested);

			EventManager.Instance.Publish(new RiftLifeChangedEvent(_life, MaxLife));
			publishGauge();

			while (isRunning() == true)
			{
				Table_RiftDungeon.Row row = DungeonProgress.FindRiftWave(wave);
				if (row == null)
				{
					Debug.LogError($"[RiftDungeonMode] RiftDungeon 행이 없습니다 — Wave {wave}");
					return;
				}

				await runWaveAsync(wave, row, ct);
				if (isRunning() == false)
				{
					break;
				}

				_clearedWave = wave;
				wave++;
			}

			// 라이프가 다했으면 정산 종료. Stop(제한시간)이면 결과는 Director 가 이미 정했다.
			if (_life <= 0)
			{
				_result = DungeonResult.Cleared;
			}
		}

		protected override void OnFinished()
		{
			if (_onMonsterKill != null)
			{
				EventManager.Instance.Unsubscribe<MonsterKillEvent>(_onMonsterKill);
				_onMonsterKill = null;
			}

			if (_onSkillUseRequested != null)
			{
				EventManager.Instance.Unsubscribe<RiftSkillUseRequestedEvent>(_onSkillUseRequested);
				_onSkillUseRequested = null;
			}
		}

		// ── 웨이브 ────────────────────────────────────────────────────

		private async UniTask runWaveAsync(int wave, Table_RiftDungeon.Row row, CancellationToken ct)
		{
			int[] groups = GetSpawnGroups(row.MonsterSpawnGroupIDs);

			int total = 0;
			for (int i = 0; i < groups.Length; i++)
			{
				total += SpawnGroupRunner.CountGroup(groups[i]);
			}

			if (total <= 0)
			{
				Debug.LogError($"[RiftDungeonMode] 웨이브에 소환할 몬스터가 없습니다 — RiftDungeon:{row.ID} (Wave {wave})");
				_stopped = true;
				return;
			}

			EventManager.Instance.Publish(new WaveStartedEvent(wave, 0, total));

			for (int g = 0; g < groups.Length; g++)
			{
				int count = SpawnGroupRunner.CountGroup(groups[g]);
				for (int n = 0; n < count; n++)
				{
					Monster monster = await SpawnGroupRunner.SpawnOneFromGroupAsync(groups[g], row.MonsterLevel);
					ct.ThrowIfCancellationRequested();

					if (monster != null && monster.Brain != null)
					{
						monster.Brain.SetBehaviorOverride(new MonsterPathBehavior(_path, _onMonsterArrived));
					}

					await waitAsync(SpawnInterval, ct);
					if (isRunning() == false)
					{
						return;
					}
				}
			}

			// 이 웨이브 몬스터가 모두 사라질 때까지(처치 또는 도착)
			while (isRunning() == true && MonsterSpawnManager.Instance.ActiveCount > 0)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
				processArrived();
			}
		}

		// 기다리는 동안에도 도착 처리를 매 프레임 한다 — 라이프가 다하면 즉시 빠져나간다.
		private async UniTask waitAsync(float seconds, CancellationToken ct)
		{
			float elapsed = 0f;
			while (elapsed < seconds && isRunning() == true)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
				elapsed += Time.deltaTime;
				processArrived();
			}
		}

		private bool isRunning()
		{
			return _stopped == false && _life > 0;
		}

		// ── 도착 ──────────────────────────────────────────────────────

		private void onMonsterArrived(Monster monster)
		{
			_arrived.Add(monster);
		}

		private void processArrived()
		{
			if (_arrived.Count == 0)
			{
				return;
			}

			for (int i = 0; i < _arrived.Count; i++)
			{
				MonsterSpawnManager.Instance.RemoveArrived(_arrived[i]);

				if (_stopped == false && _life > 0)
				{
					_life--;
				}
			}

			_arrived.Clear();
			EventManager.Instance.Publish(new RiftLifeChangedEvent(_life, MaxLife));
		}

		// 스폰 슬롯 → 웨이포인트 순서의 경로. 스폰 슬롯은 출발점일 뿐이라 경로에 넣지 않는다.
		private bool collectPath()
		{
			_path.Clear();

			if (MapManager.HasInstance == false)
			{
				return false;
			}

			IReadOnlyList<DungeonWaypoint> points = MapManager.Instance.GetWaypointsOfCurrentMap();
			for (int i = 0; i < points.Count; i++)
			{
				_path.Add(points[i].Position);
			}

			return _path.Count > 0;
		}

		// ── 스킬 게이지 ───────────────────────────────────────────────

		private void setupSkill(int riftSkillId)
		{
			_riftSkillId = riftSkillId;
			_skill = Table_RiftSkill.Get(riftSkillId);
			if (_skill == null)
			{
				Debug.LogError($"[RiftDungeonMode] RiftSkill 행이 없습니다 — ID {riftSkillId}");
			}
		}

		private void onMonsterKill(MonsterKillEvent evt)
		{
			if (_stopped == true || _skill == null)
			{
				return;
			}

			Table_Monster.Row row = Table_Monster.Get(evt.MonsterID);
			bool strong = (row != null && (row.MonsterType == MonsterType.Elite || row.MonsterType == MonsterType.Boss));

			_gauge += strong ? EliteGauge : NormalGauge;

			int max = getMaxGauge();
			if (_gauge > max)
			{
				_gauge = max;
			}

			publishGauge();
		}

		// 게이지가 ReqGauge 이상이면 한 번 발동하고 ReqGauge 만큼 소모한다.
		// 히어로에 등록하지 않고 바로 실행한다 — 등록하면 자동전투 AI 가 게이지와 무관하게 써 버린다.
		private void onSkillUseRequested(RiftSkillUseRequestedEvent evt)
		{
			if (_stopped == true || _skill == null || _skill.ReqGauge <= 0 || _gauge < _skill.ReqGauge)
			{
				return;
			}

			UnitBase hero = findAliveHero();
			if (hero == null)
			{
				return;
			}

			// 평타 모션 중이면 끊고 바로 쓴다 — 누른 즉시 나가야 한다.
			if (hero.SkillContainer != null)
			{
				hero.SkillContainer.CancelAction();
			}

			SkillExecutor.Execute(_skill.SkillID, hero, 1f);

			_gauge -= _skill.ReqGauge;
			publishGauge();
		}

		// MaxGauge 가 비어 있으면 1회분만 모은다.
		private int getMaxGauge()
		{
			if (_skill == null)
			{
				return 0;
			}

			return (_skill.MaxGauge > _skill.ReqGauge) ? _skill.MaxGauge : _skill.ReqGauge;
		}

		private void publishGauge()
		{
			int req = (_skill != null) ? _skill.ReqGauge : 0;
			EventManager.Instance.Publish(new RiftGaugeChangedEvent(_riftSkillId, _gauge, req, getMaxGauge()));
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
