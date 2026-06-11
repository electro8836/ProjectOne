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

			// 히스테리시스 정지 판정
			float range = GetAttackRange(self);
			float dist = dirToTarget.magnitude;

			// 물리 접촉 거리와 공격 사거리 중 더 큰 값으로 정지 — ScanParam1 이 작아도 닿으면 정지
			float stoppingDist = Mathf.Max(range, self.Radius + target.Radius);

			if (_approaching == true)
			{
				bool inRange = dist <= stoppingDist;
				bool inQueue = inRange == false && IsQueuedBehindAlly(self, selfPos, target, stoppingDist);
				if (inRange || inQueue)
				{
					_approaching = false;
				}
			}
			else
			{
				if (dist > range * _hysteresis)
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

		// 기본공격 스킬의 ScanParam1 을 정지 사거리로 사용 (없으면 폴백)
		private static float GetAttackRange(UnitBase self)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return _fallbackRange;
			}

			SkillInfo basic = sc.GetBasicAttack();
			if (basic == SkillInfo.None)
			{
				return _fallbackRange;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(basic);
			if (row == null || row.ScanParam1 <= 0f)
			{
				return _fallbackRange;
			}

			return row.ScanParam1;
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
