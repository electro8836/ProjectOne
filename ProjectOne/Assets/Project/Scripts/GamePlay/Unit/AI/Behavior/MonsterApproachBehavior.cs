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

		// 분산 조향 가중치 — 이웃 반발 벡터를 합산해 슬라이딩의 비스듬한 각도를 만든다
		private const float _separationWeight = 0.7f;

		// 기본공격 사거리를 못 구할 때의 폴백 정지 거리
		private const float _fallbackRange = 1f;

		// 타겟/사거리/스킬 등 무거운 판단의 갱신 주기(초) — 이동은 매 프레임이라 회피는 즉각, 판단만 분산
		private const float _decisionInterval = 0.12f;

		// 인스턴스별 상태 — 몬스터마다 behavior 1개라 히스테리시스 상태를 안전하게 보관
		private bool _approaching = true;

		// 의사결정 누적 시간 — 시작 오프셋을 무작위로 두어 다수 몬스터의 판단이 한 프레임에 몰리지 않게 분산
		private float _decisionAccum = Random.Range(0f, _decisionInterval);

		// 의사결정에서 산출한 접근 방향 — 매 프레임 이동이 이 값 + 최신 분리벡터로 조향한다
		private Vector2 _cachedApproachDir;

		// 기본공격 정보 캐시 — 사거리/발사체여부 모두 불변이라 최초 1회만 테이블 조회
		private float _cachedRange = -1f;
		private bool _basicIsProjectile;

		// IsQueuedBehindAlly 인접 조회 재사용 버퍼 (GC 회피 — 람다 금지)
		private static readonly List<UnitBase> _queueBuffer = new List<UnitBase>(16);

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

			// 접근 — 캐시된 접근 방향 + 최신 분리 벡터(UnitSimulator 가 매 프레임 갱신)로 조향
			Vector2 separation = self.CachedSeparation;
			Vector2 final = _cachedApproachDir + separation * _separationWeight;
			if (final.sqrMagnitude < 1e-6f)
			{
				final = _cachedApproachDir;
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
				Table_SkillInfo.Row basicRow = GetBasicAttackRow(self);
				_cachedRange = GetAttackRange(basicRow);
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
				bool inQueue = inRange == false && IsQueuedBehindAlly(self, selfPos, target, stoppingDist);
				if ((inRange || inQueue) && HasClearShot(self, target) == true)
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

		// 기본공격 스킬의 ScanParam1 을 정지 사거리로 사용 (없으면 폴백)
		private static float GetAttackRange(Table_SkillInfo.Row basicRow)
		{
			if (basicRow == null || basicRow.ScanParam1 <= 0f)
			{
				return _fallbackRange;
			}

			return basicRow.ScanParam1;
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

		// 자신과 인접한 아군이 이미 히어로 정지 거리 내에 있으면 true — 뒷줄 몬스터 대기 정지.
		// 전체 몬스터 순회 대신 SpatialHash 로 인접 후보(3×3 셀)만 본다 — 다수 몬스터 O(N²) 완화.
		private static bool IsQueuedBehindAlly(UnitBase self, Vector2 selfPos, UnitBase hero, float stoppingDist)
		{
			if (UnitContainer.Instance == null)
			{
				return false;
			}

			Vector2 heroPos = hero.CachedPos;
			float stoppingDistSqr = stoppingDist * stoppingDist;
			UnitContainer.Instance.SpatialHash.Query(selfPos, _queueBuffer);
			for (int i = 0; i < _queueBuffer.Count; i++)
			{
				UnitBase m = _queueBuffer[i];
				if (m == null || m == self || m.IsDead == true || m.Faction != self.Faction)
				{
					continue;
				}

				// 자신과 인접한 아군만 (접촉 반경 2배 이내)
				float contactR = self.CachedRadius + m.CachedRadius;
				Vector2 mPos = m.CachedPos;
				if ((mPos - selfPos).sqrMagnitude > contactR * contactR * 4f)
				{
					continue;
				}

				// 그 아군이 히어로 정지 거리 내에 있으면 → 나는 줄을 서야 함
				if ((mPos - heroPos).sqrMagnitude <= stoppingDistSqr)
				{
					return true;
				}
			}

			return false;
		}
	}
}
