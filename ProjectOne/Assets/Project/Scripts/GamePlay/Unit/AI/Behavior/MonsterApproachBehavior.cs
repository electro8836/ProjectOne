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
	public sealed class MonsterApproachBehavior : IAiBehavior
	{
		// 사거리 안이면 정지, 사거리 * _hysteresis 를 넘어서야 다시 접근 — 경계 떨림 방지
		private const float _hysteresis = 1.1f;

		// 분산 조향 가중치 — 이웃 반발 벡터를 합산해 슬라이딩의 비스듬한 각도를 만든다
		private const float _separationWeight = 0.7f;

		// 기본공격 사거리를 못 구할 때의 폴백 정지 거리
		private const float _fallbackRange = 1f;

		// 인스턴스별 상태 — 몬스터마다 behavior 1개라 히스테리시스 상태를 안전하게 보관
		private bool _approaching = true;

		// 기본공격 정보 캐시 — 사거리/발사체여부 모두 불변이라 최초 1회만 테이블 조회
		private float _cachedRange = -1f;
		private bool _basicIsProjectile;

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			UnitBase target = FindNearestEnemyHero(self);
			bb.Target = target;
			if (target == null)
			{
				self.Mover.Stop();
				return;
			}

			Vector2 selfPos = self.CachedPos;
			Vector2 dirToTarget = target.CachedPos - selfPos;

			// 기본공격 사거리·발사체여부는 불변 — 최초 1회만 테이블 조회해 캐시 (100마리 매 틱 조회 방지)
			if (_cachedRange < 0f)
			{
				Table_SkillInfo.Row basicRow = GetBasicAttackRow(self);
				_cachedRange = GetAttackRange(basicRow);
				_basicIsProjectile = (basicRow != null && SkillSelector.IsProjectileSkill(basicRow) == true);
			}

			float range = _cachedRange;
			float dist = dirToTarget.magnitude;

			// 물리 접촉 거리와 공격 사거리 중 더 큰 값으로 정지 — ScanParam1 이 작아도 닿으면 정지
			float stoppingDist = Mathf.Max(range, self.Radius + target.Radius);

			// LoS(HasClearShot)는 정지 판단이 필요한 순간에만 계산한다 — 접근 중(사거리 밖) 대다수는 스킵해 100마리 부하를 줄인다.
			// 발사체 기본공격이 벽에 가려져 있으면 정지해도 헛스킬이므로, 그때만 접근을 계속해 시야가 트일 위치로 이동한다.
			if (_approaching == true)
			{
				bool inRange = dist <= stoppingDist;
				bool inQueue = inRange == false && IsQueuedBehindAlly(self, selfPos, target, stoppingDist);
				if ((inRange || inQueue) && HasClearShot(self, target) == true)
				{
					_approaching = false;
				}
			}
			else
			{
				// 거리 히스테리시스로 재접근하거나, 시야가 막히면(LoS) 즉시 재접근
				if (dist > range * _hysteresis || HasClearShot(self, target) == false)
				{
					_approaching = true;
				}
			}

			if (_approaching == false)
			{
				self.Mover.Stop();
				// 정지(공격) 시엔 타겟을 응시 — Sector/Line 스킬이 타겟을 조준하도록 시전 전에 설정
				self.Mover.SetFacing(dirToTarget);
				// 정지 상태에서만 스킬 시전 — 이동 중 공격 방지
				SkillSelector.Select(self, false);
				return;
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

			// 분리 벡터는 UnitSimulator 가 프레임당 1회 배치 계산해 둔 값을 읽는다
			Vector2 separation = self.CachedSeparation;
			Vector2 final = approach + separation * _separationWeight;
			if (final.sqrMagnitude < 1e-6f)
			{
				final = approach;
			}

			self.Mover.Move(final, self.Stats.GetStat(StatInfo.MoveSpeed));
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
		// 호출자(Tick)가 정지 판단이 필요한 순간에만 부르므로 접근 중 몬스터는 LoS 계산을 타지 않는다.
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

		// 자신과 인접한 아군이 이미 히어로 정지 거리 내에 있으면 true — 뒷줄 몬스터 대기 정지
		private static bool IsQueuedBehindAlly(UnitBase self, Vector2 selfPos, UnitBase hero, float stoppingDist)
		{
			if (UnitContainer.Instance == null)
			{
				return false;
			}

			Vector2 heroPos = hero.CachedPos;
			float stoppingDistSqr = stoppingDist * stoppingDist;
			IReadOnlyList<UnitBase> monsters = UnitContainer.Instance.GetByType(UnitType.Monster);
			for (int i = 0; i < monsters.Count; i++)
			{
				UnitBase m = monsters[i];
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
