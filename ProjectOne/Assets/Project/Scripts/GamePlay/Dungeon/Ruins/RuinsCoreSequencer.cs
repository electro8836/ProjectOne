using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Skill;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 유적 방어 시스템 — 코어의 HP 구간에 따라 던전 전체에 공격을 순서대로 내린다 (RuinsDungeonMode 가 소유).
	//
	// 공격은 코어 몸에서 나가지 않는다. 가로줄은 공격 영역 전체, 원형·도넛은 맵의 지점, 낙뢰는 유저 위치가 기준이다.
	// 데미지만 코어를 시전자로 넘겨 코어 레벨(단계)의 공격력을 받는다.
	//
	// 흐름
	//   페이즈 — HP 비율이 다음 페이즈의 HpThreshold 이하가 되는 즉시 넘어가고, SpawnGroupIndex 가 있으면 1회 소환한다.
	//   행동   — 현재 공격이 모두 발동하고 AfterDelay 가 지나면 다음 공격을 고른다.
	//            페이즈가 바뀌었으면 새 페이즈 순서(HazardIDs)의 처음부터 쓴다. 끝에 닿으면 처음으로 돌아간다.
	//   공격   — 예고 장판을 WarnTime 동안 보여준 뒤 그 모양 안의 히어로에게 DamageEffectID 를 적용한다.
	//
	// 공격 하나하나에 코루틴·UniTask 를 만들지 않고 타이머 목록으로 틱 처리한다.
	public sealed class RuinsCoreSequencer
	{
		private enum Shape
		{
			Circle,
			Ring,
			Rect,
		}

		// 예고 중인 공격 1개
		private sealed class Strike
		{
			public Shape shape;
			public Vector2 center;
			public float inner;
			public float outer;
			public Vector2 min;
			public Vector2 max;
			public float warnTime;
			public float remain;
			public SkillEffect effect;
			public RuinsHazardTelegraph telegraph;
		}

		private UnitBase _core;
		private RuinsMap _map;
		private int[] _spawnGroups;
		private int _level;

		private readonly List<Table_RuinsCorePhase.Row> _phases = new List<Table_RuinsCorePhase.Row>(4);
		private int _phaseIndex = -1;
		private int _actionIndex;

		// 진행 중인 공격(낙뢰처럼 반복하는 것 포함). 없으면 null.
		private Table_RuinsHazard.Row _running;
		private int _repeatLeft;
		private float _repeatTimer;
		private float _restTimer;

		private readonly List<Strike> _strikes = new List<Strike>(8);
		private readonly List<Strike> _strikePool = new List<Strike>(8);
		private readonly List<RuinsHazardTelegraph> _telegraphPool = new List<RuinsHazardTelegraph>(8);
		private readonly List<UnitBase> _hitBuffer = new List<UnitBase>(2);

		public void Setup(UnitBase core, RuinsMap map, int phaseGroupId, int[] spawnGroups, int level)
		{
			_core = core;
			_map = map;
			_spawnGroups = spawnGroups;
			_level = level;

			collectPhases(phaseGroupId);
			_phaseIndex = (_phases.Count > 0) ? 0 : -1;
			_actionIndex = 0;

			if (_phases.Count == 0)
			{
				Debug.LogError($"[RuinsCoreSequencer] RuinsCorePhase 에 GroupID {phaseGroupId} 행이 없습니다 — 방어 시스템이 공격하지 않습니다.");
			}
		}

		public void Tick(float dt)
		{
			if (_core == null || _core.IsDead == true || _phaseIndex < 0)
			{
				return;
			}

			updatePhase();
			tickStrikes(dt);

			if (_running != null)
			{
				tickRunning(dt);
				return;
			}

			if (_restTimer > 0f)
			{
				_restTimer -= dt;
				return;
			}

			beginNextAction();
		}

		// 코어 파괴·모드 종료 — 예고 중인 공격을 모두 거둔다(발동시키지 않는다).
		public void Cancel()
		{
			for (int i = 0; i < _strikes.Count; i++)
			{
				releaseStrike(_strikes[i]);
			}

			_strikes.Clear();
			_running = null;
			_repeatLeft = 0;
		}

		// ── 페이즈 ────────────────────────────────────────────────────

		private void collectPhases(int groupId)
		{
			_phases.Clear();

			Dictionary<int, Table_RuinsCorePhase.Row>.Enumerator e = Table_RuinsCorePhase.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_RuinsCorePhase.Row row = e.Current.Value;
				if (row.GroupID == groupId)
				{
					_phases.Add(row);
				}
			}

			// 행 수가 한 자릿수라 삽입 정렬로 충분하다 — 람다 비교자를 만들지 않는다.
			for (int i = 1; i < _phases.Count; i++)
			{
				Table_RuinsCorePhase.Row key = _phases[i];
				int j = i - 1;
				while (j >= 0 && _phases[j].PhaseOrder > key.PhaseOrder)
				{
					_phases[j + 1] = _phases[j];
					j--;
				}

				_phases[j + 1] = key;
			}
		}

		// 한 번에 큰 피해로 여러 구간을 건너뛰어도 구간마다 진입 처리(소환)를 한다.
		private void updatePhase()
		{
			float ratio = hpRatio(_core);
			while (_phaseIndex + 1 < _phases.Count && ratio <= _phases[_phaseIndex + 1].HpThreshold)
			{
				_phaseIndex++;
				_actionIndex = 0;
				enterPhase(_phases[_phaseIndex]);
			}
		}

		private void enterPhase(Table_RuinsCorePhase.Row phase)
		{
			int index = phase.SpawnGroupIndex - 1;
			if (index < 0)
			{
				return;
			}

			if (_spawnGroups == null || index >= _spawnGroups.Length)
			{
				Debug.LogError($"[RuinsCoreSequencer] 페이즈 {phase.PhaseOrder} 의 SpawnGroupIndex {phase.SpawnGroupIndex} 에 해당하는 스폰 그룹이 없습니다.");
				return;
			}

			SpawnGroupRunner.SpawnGroupAt(_spawnGroups[index], _level, _map.SummonSlots);
		}

		// ── 행동 ──────────────────────────────────────────────────────

		private void beginNextAction()
		{
			int[] order = _phases[_phaseIndex].HazardIDs;
			if (order == null || order.Length == 0)
			{
				return;
			}

			int hazardId = order[_actionIndex % order.Length];
			_actionIndex = (_actionIndex + 1) % order.Length;

			Table_RuinsHazard.Row hazard = Table_RuinsHazard.Get(hazardId);
			if (hazard == null)
			{
				Debug.LogError($"[RuinsCoreSequencer] RuinsHazard {hazardId} 행이 없습니다 — 건너뜁니다.");
				_restTimer = 1f;
				return;
			}

			_running = hazard;
			_repeatLeft = 0;

			switch (hazard.HazardType)
			{
				case RuinsHazardType.HorizontalLine:
					spawnLines(hazard);
					break;
				case RuinsHazardType.PointCircle:
				case RuinsHazardType.PointDonut:
					spawnAtPoints(hazard);
					break;
				case RuinsHazardType.TargetCircle:
					_repeatLeft = Mathf.Max(1, hazard.Count);
					_repeatTimer = 0f;
					break;
				default:
					Debug.LogError($"[RuinsCoreSequencer] 처리할 수 없는 HazardType: {hazard.HazardType} — RuinsHazard {hazard.ID}");
					break;
			}
		}

		// 반복 공격(낙뢰)의 다음 타를 내리고, 모든 타가 발동하면 휴식에 들어간다.
		private void tickRunning(float dt)
		{
			if (_repeatLeft > 0)
			{
				_repeatTimer -= dt;
				if (_repeatTimer <= 0f)
				{
					spawnAtHero(_running);
					_repeatLeft--;
					_repeatTimer = _running.RepeatInterval;
				}
			}

			if (_repeatLeft > 0 || _strikes.Count > 0)
			{
				return;
			}

			_restTimer = _running.AfterDelay;
			_running = null;
		}

		// 공격 영역 높이를 Count 로 등분해 칸마다 가운데에 가로줄을 긋는다. 두께는 Radius × 2.
		private void spawnLines(Table_RuinsHazard.Row hazard)
		{
			Vector2 min;
			Vector2 max;
			if (_map.GetHazardRect(out min, out max) == false)
			{
				Debug.LogError("[RuinsCoreSequencer] RuinsMap 에 공격 영역(_hazardArea)이 없습니다 — 가로줄 공격을 건너뜁니다.");
				return;
			}

			int count = Mathf.Max(1, hazard.Count);
			float step = (max.y - min.y) / count;
			for (int i = 0; i < count; i++)
			{
				float y = min.y + step * (i + 0.5f);
				Strike s = rentStrike(hazard, Shape.Rect);
				s.min = new Vector2(min.x, y - hazard.Radius);
				s.max = new Vector2(max.x, y + hazard.Radius);
				s.telegraph.ShowRect(s.min, s.max);
				_strikes.Add(s);
			}
		}

		private void spawnAtPoints(Table_RuinsHazard.Row hazard)
		{
			Shape shape = (hazard.HazardType == RuinsHazardType.PointDonut) ? Shape.Ring : Shape.Circle;

			int spawned = 0;
			IReadOnlyList<RuinsHazardPoint> points = _map.HazardPoints;
			for (int i = 0; i < points.Count; i++)
			{
				RuinsHazardPoint point = points[i];
				if (point == null || (hazard.PointGroup > 0 && point.Group != hazard.PointGroup))
				{
					continue;
				}

				spawnRound(hazard, shape, point.Position);
				spawned++;
			}

			if (spawned == 0)
			{
				Debug.LogWarning($"[RuinsCoreSequencer] PointGroup {hazard.PointGroup} 에 해당하는 공격 지점이 맵에 없습니다 — RuinsHazard {hazard.ID}");
			}
		}

		// 지금 유저가 선 자리에 떨어진다 — 예고 동안 벗어나면 피한다.
		private void spawnAtHero(Table_RuinsHazard.Row hazard)
		{
			UnitBase hero = findAliveHero();
			if (hero == null)
			{
				return;
			}

			spawnRound(hazard, Shape.Circle, hero.CachedPos);
		}

		private void spawnRound(Table_RuinsHazard.Row hazard, Shape shape, Vector2 center)
		{
			Strike s = rentStrike(hazard, shape);
			s.center = center;
			s.inner = (shape == Shape.Ring) ? hazard.InnerRadius : 0f;
			s.outer = hazard.Radius;

			if (shape == Shape.Ring)
			{
				s.telegraph.ShowRing(center, s.inner, s.outer);
			}
			else
			{
				s.telegraph.ShowCircle(center, s.outer);
			}

			_strikes.Add(s);
		}

		// ── 예고 → 발동 ───────────────────────────────────────────────

		private void tickStrikes(float dt)
		{
			for (int i = _strikes.Count - 1; i >= 0; i--)
			{
				Strike s = _strikes[i];
				s.remain -= dt;

				if (s.remain > 0f)
				{
					float t = (s.warnTime > 0f) ? 1f - s.remain / s.warnTime : 1f;
					s.telegraph.SetProgress(t);
					continue;
				}

				fire(s);
				_strikes.RemoveAt(i);
				releaseStrike(s);
			}
		}

		// 판정은 발 위치(CachedPos) 기준이다 — 예고 장판 안에 서 있으면 맞는다.
		private void fire(Strike s)
		{
			_hitBuffer.Clear();

			if (UnitManager.HasInstance == true)
			{
				IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
				for (int i = 0; i < heroes.Count; i++)
				{
					UnitBase hero = heroes[i];
					if (hero != null && hero.IsDead == false && contains(s, hero.CachedPos) == true)
					{
						_hitBuffer.Add(hero);
					}
				}
			}

			if (_hitBuffer.Count > 0)
			{
				SkillEffectApplier.Apply(s.effect, _core, EDT.Skill.None, _hitBuffer, 0);
			}

			_hitBuffer.Clear();
		}

		private static bool contains(Strike s, Vector2 pos)
		{
			switch (s.shape)
			{
				case Shape.Rect:
					return pos.x >= s.min.x && pos.x <= s.max.x && pos.y >= s.min.y && pos.y <= s.max.y;
				case Shape.Ring:
				{
					float sqr = (pos - s.center).sqrMagnitude;
					return sqr >= s.inner * s.inner && sqr <= s.outer * s.outer;
				}
				default:
					return (pos - s.center).sqrMagnitude <= s.outer * s.outer;
			}
		}

		// ── 풀 ────────────────────────────────────────────────────────

		private Strike rentStrike(Table_RuinsHazard.Row hazard, Shape shape)
		{
			Strike s;
			if (_strikePool.Count > 0)
			{
				s = _strikePool[_strikePool.Count - 1];
				_strikePool.RemoveAt(_strikePool.Count - 1);
			}
			else
			{
				s = new Strike();
			}

			s.shape = shape;
			s.warnTime = hazard.WarnTime;
			s.remain = hazard.WarnTime;
			s.effect = hazard.DamageEffectID;
			s.telegraph = rentTelegraph();
			return s;
		}

		private void releaseStrike(Strike s)
		{
			if (s.telegraph != null)
			{
				s.telegraph.Hide();
				_telegraphPool.Add(s.telegraph);
				s.telegraph = null;
			}

			_strikePool.Add(s);
		}

		// 예고 장판은 맵 밑에 만든다 — 맵이 파괴될 때 함께 사라진다.
		private RuinsHazardTelegraph rentTelegraph()
		{
			while (_telegraphPool.Count > 0)
			{
				RuinsHazardTelegraph pooled = _telegraphPool[_telegraphPool.Count - 1];
				_telegraphPool.RemoveAt(_telegraphPool.Count - 1);
				if (pooled != null)
				{
					return pooled;
				}
			}

			return RuinsHazardTelegraph.Create(_map.transform);
		}

		// ── 공통 ──────────────────────────────────────────────────────

		private static float hpRatio(UnitBase unit)
		{
			if (unit.Vitals == null || unit.Stats == null)
			{
				return 1f;
			}

			float maxHp = unit.Stats.GetStat(Stat.Stat_MaxHp);
			if (maxHp <= 0f)
			{
				return 1f;
			}

			return unit.Vitals.Hp / maxHp;
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
