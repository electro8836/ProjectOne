using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Map;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 몬스터 접근형 전략 — 가장 가까운 히어로로 플로우필드 접근, 기본공격 사거리에서 정지하고 스킬 자동 시전.
	// 정지/재접근은 히스테리시스 밴드로 떨림 방지. 몬스터끼리 겹치면 UnitMover.ComputeCircleSlide 가
	// 원형 충돌 법선 기반으로 미끄러지게 하여 자연스러운 포위 형성.
	// 비용 분산: 타겟 탐색/사거리·시야 판정/스킬 시전은 _decisionInterval 주기로만 수행하고(다수 몬스터 O(N²) 완화),
	// 이동(접근 방향 + 분리)·시선은 매 프레임 갱신해 회피 반응성을 유지한다.
	public sealed class MonsterApproachBehavior : IAiBehavior
	{
		// 사거리 안이면 정지, 사거리 * _hysteresis 를 넘어서야 다시 접근 — 경계 떨림 방지
		private const float _hysteresis = 1.1f;

		// 분산 조향 가중치 — 이웃 반발을 접근 방향(radial=1)과 균형 있게 실어, 앞이 막히면 측면 성분으로
		// 빈 자리로 돌아 들어가게 한다(정적 포위). Move 가 방향을 정규화하므로 속도는 일정.
		private const float _separationWeight = 1.0f;

		// 멈춰 대기 임계 — 둘러싸여 최종 이동 벡터가 이보다 작아지면 정지(settle), _resumeThreshold 를
		// 넘어야 다시 이동(히스테리시스로 멈춤↔이동 깜빡임 방지)
		private const float _settleThreshold = 0.3f;
		private const float _resumeThreshold = 0.55f;

		// 접선(회전) 세기 — 정면이 막히면 후퇴량을 이 비율로 측면 회전으로 바꿔 빈 공간을 채운다
		private const float _tangentWeight = 1.0f;

		// 회전 방향 전환 임계 — side(접선 분리 성분)가 이 값을 반대로 넘어야만 회전 방향을 바꾼다.
		// 클수록 방향 전환이 둔해져 뒷줄의 좌우 와리가리가 줄어든다(반응성은 약간 희생)
		private const float _orbitSwitch = 0.35f;

		// settle 허용 거리 배수 — 사거리의 이 배수 이내에서만 멈춤(멀리서 조기 정지 방지)
		private const float _settleRangeMul = 2.5f;

		// 기본공격 사거리를 못 구할 때의 폴백 정지 거리
		private const float _fallbackRange = 1f;

		// 타겟/사거리/스킬 등 무거운 판단의 갱신 주기(초) — 이동은 매 프레임이라 회피는 즉각, 판단만 분산
		private const float _decisionInterval = 0.25f;

		// 인스턴스별 상태 — 몬스터마다 behavior 1개라 히스테리시스 상태를 안전하게 보관
		private bool _approaching = true;

		// 둘러싸여 더 못 들어가면 그 자리에 멈춰 대기하는 상태 — 빈자리가 생기면 해제하고 다시 전진
		private bool _settled;

		// 정면 대칭 교착 시 회전 방향 — 인스턴스마다 고정으로 좌/우를 갈라 빙글 교착을 막는다
		private readonly float _orbitBias = (Random.value < 0.5f) ? -1f : 1f;

		// 현재 회전 방향(±1) — 히스테리시스로 유지해 좌우 와리가리를 막는다. 0이면 첫 사용 때 _orbitBias 로 시드
		private float _orbitDir;

		// 의사결정 누적 시간 — 시작 오프셋을 무작위로 두어 다수 몬스터의 판단이 한 프레임에 몰리지 않게 분산
		private float _decisionAccum = Random.Range(0f, _decisionInterval);

		// 의사결정에서 산출한 접근 방향 — 매 프레임 이동이 이 값 + 최신 분리벡터로 조향한다
		private Vector2 _cachedApproachDir;

		// 기본공격 정보 캐시 — 사거리/발사체여부 모두 불변이라 최초 1회만 테이블 조회
		private float _cachedRange = -1f;
		private bool _basicIsProjectile;

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			// 판단 주기 도래 시에만 타겟/정지·접근 상태/접근 방향을 재계산 (무거운 탐색을 분산)
			_decisionAccum += dt;
			bool decide = _decisionAccum >= _decisionInterval;
			if (decide == true)
			{
				_decisionAccum -= _decisionInterval;
				Decide(self, bb);
			}

			UnitBase target = bb.Target;
			if (target == null)
			{
				self.Mover.Stop();
				return;
			}

			// 정지(공격) — 타겟을 응시하고, 판단 주기마다 스킬을 시전한다
			if (_approaching == false)
			{
				self.Mover.Stop();
				// Sector/Line 스킬이 타겟을 조준하도록 시전 전에 시선을 맞춘다
				self.Mover.SetFacing(target.CachedPos - self.CachedPos);
				if (decide == true)
				{
					SkillSelector.Select(self, false);
				}

				return;
			}

			// 접근 — 사거리 안으로는 전진하지 않는다(오버슈트로 너무 붙는 것 방지). 분리·접선은 그대로 둬 측면 정렬 유지.
			// 사거리(=max(스킬 사거리, 반지름합))에 들어서면 radial 0 → Decide 가 inRange 로 공격 전환한다.
			Vector2 radial = _cachedApproachDir;
			float distToTarget = (target.CachedPos - self.CachedPos).magnitude;
			float stopDist = Mathf.Max(_cachedRange, self.Radius + target.Radius);
			if (distToTarget <= stopDist)
			{
				radial = Vector2.zero;
			}

			Vector2 final = radial + self.CachedSeparation * _separationWeight;

			// 정면이 막히면(분리가 전진을 거스르면) 그만큼 플레이어 주위로 돌아 측면 빈 공간으로 —
			// final 이 0 으로 정체되기 전에(back<0 보다 일찍) 측면 회피를 시작해 뒷줄이 멈추지 않게 한다
			float opposing = Vector2.Dot(self.CachedSeparation, radial);
			if (opposing < 0f)
			{
				Vector2 perp = new Vector2(-radial.y, radial.x);
				float side = Vector2.Dot(self.CachedSeparation, perp);
				// 회전 방향 히스테리시스 — side 가 임계를 넘게 반대로 가야만 전환(와리가리 방지)
				if (side > _orbitSwitch)
				{
					_orbitDir = 1f;
				}
				else if (side < -_orbitSwitch)
				{
					_orbitDir = -1f;
				}
				else if (_orbitDir == 0f)
				{
					_orbitDir = _orbitBias;
				}

				final += perp * _orbitDir * (-opposing) * _tangentWeight;
			}

			// 후퇴 방지 — 그래도 전진 반대로 향하면 그 성분만 제거(전진 또는 측면만)
			float back = Vector2.Dot(final, radial);
			if (back < 0f)
			{
				final -= back * radial;
			}

			// 멈춰 대기 — 사거리 근처에서 사방(접선 포함)이 막혔을 때만 정지. 멀면 안 멈추고 빈 공간을 채우러 간다.
			float mag = final.magnitude;
			float settleRange = _cachedRange * _settleRangeMul;
			bool nearEnough = (target.CachedPos - self.CachedPos).sqrMagnitude < settleRange * settleRange;
			if (_settled == true)
			{
				if (mag > _resumeThreshold || nearEnough == false)
				{
					_settled = false;
				}
			}
			else if (mag < _settleThreshold && nearEnough == true)
			{
				_settled = true;
			}

			if (_settled == true)
			{
				self.Mover.Stop();
				self.Mover.SetFacing(target.CachedPos - self.CachedPos);
				return;
			}

			self.Mover.Move(final, self.Stats.GetStat(StatInfo.MoveSpeed));
		}

		// 주기적 의사결정 — 타겟 탐색, 정지/재접근 전환(히스테리시스+시야), 접근 방향 산출
		private void Decide(UnitBase self, Blackboard bb)
		{
			UnitBase target = FindNearestEnemyHero(self);
			bb.Target = target;
			if (target == null)
			{
				return;
			}

			Vector2 selfPos = self.CachedPos;
			Vector2 dirToTarget = target.CachedPos - selfPos;

			// 기본공격 사거리·발사체여부는 불변 — 최초 1회만 테이블 조회해 캐시 (다수 몬스터 매 판단 조회 방지)
			if (_cachedRange < 0f)
			{
				_cachedRange = GetStoppingRange(self);
				Table_SkillInfo.Row basicRow = GetBasicAttackRow(self);
				_basicIsProjectile = (basicRow != null && SkillSelector.IsProjectileSkill(basicRow) == true);
			}

			float range = _cachedRange;
			float distSqr = dirToTarget.sqrMagnitude;

			// 물리 접촉 거리와 공격 사거리 중 더 큰 값으로 정지 — ScanParam1 이 작아도 닿으면 정지
			float stoppingDist = Mathf.Max(range, self.Radius + target.Radius);
			float stoppingDistSqr = stoppingDist * stoppingDist;

			// LoS(HasClearShot)는 정지 판단이 필요한 순간에만 계산한다. 발사체 기본공격이 벽에 가려져 있으면
			// 정지해도 헛스킬이므로, 그때만 접근을 계속해 시야가 트일 위치로 이동한다.
			if (_approaching == true)
			{
				bool inRange = distSqr <= stoppingDistSqr;
				if (inRange == true && HasClearShot(self, target) == true)
				{
					_approaching = false;
				}
			}
			else
			{
				// 거리 히스테리시스로 재접근하거나, 시야가 막히면(LoS) 즉시 재접근
				float reapproach = range * _hysteresis;
				if (distSqr > reapproach * reapproach || HasClearShot(self, target) == false)
				{
					_approaching = true;
				}
			}

			// 접근 방향 — 플로우필드 우선, 타겟 근처(flow 0)나 맵 없음이면 직선
			Vector2 approach = Vector2.zero;
			if (MapManager.HasInstance == true)
			{
				approach = MapManager.Instance.GetFlowDirection(selfPos);
			}

			if (approach.sqrMagnitude < 1e-6f)
			{
				approach = dirToTarget.normalized;
			}

			_cachedApproachDir = approach;
		}

		// 살아있는 적대 히어로 중 가장 가까운 대상
		private static UnitBase FindNearestEnemyHero(UnitBase self)
		{
			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			UnitBase nearest = null;
			float nearestSqr = float.MaxValue;
			Vector2 selfPos = self.CachedPos;
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase h = heroes[i];
				if (h == null || h.IsDead == true)
				{
					continue;
				}

				if (TargetResolver.IsEnemy(self.Faction, h.Faction) == false)
				{
					continue;
				}

				float sqr = (h.CachedPos - selfPos).sqrMagnitude;
				if (sqr < nearestSqr)
				{
					nearestSqr = sqr;
					nearest = h;
				}
			}

			return nearest;
		}

		// 기본공격 스킬 행 — 없으면 null
		private static Table_SkillInfo.Row GetBasicAttackRow(UnitBase self)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return null;
			}

			SkillInfo basic = sc.GetBasicAttack();
			if (basic == SkillInfo.None)
			{
				return null;
			}

			return Table_SkillInfo.Get(basic);
		}

		// 정지 사거리 — 보유 스킬 중 최소 사거리. 그 거리까지 접근해야 보유 스킬 전부가 사거리 안에 들어 시전 가능해진다.
		// 시전 가능한 사거리 스킬이 없으면 폴백.
		private static float GetStoppingRange(UnitBase self)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return _fallbackRange;
			}

			float min = sc.GetMinSkillRange();
			return (min > 0f) ? min : _fallbackRange;
		}

		// 발사체 기본공격일 때만 시야(LoS)를 따진다 — 근접/비발사체나 맵 없음이면 항상 사격 가능으로 본다.
		// 호출자(Decide)가 정지 판단이 필요한 순간에만 부르므로 접근 중 몬스터는 LoS 계산을 타지 않는다.
		private bool HasClearShot(UnitBase self, UnitBase target)
		{
			if (_basicIsProjectile == false)
			{
				return true;
			}

			if (MapManager.HasInstance == false)
			{
				return true;
			}

			return MapManager.Instance.HasLineOfSight(self.HitCenter, target.HitCenter);
		}
	}
}
