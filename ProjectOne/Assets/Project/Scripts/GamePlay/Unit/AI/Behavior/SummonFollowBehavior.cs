using UnityEngine;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 추종형 소환물 — 주인을 FollowDistance 로 따라다니다가 교전 거리에 적이 들어오면 멈춰 공격한다.
	//
	// Chase 와의 차이는 "적을 쫓아가는가"다. Follow 는 주인 곁을 떠나지 않고,
	// 사거리에 들어온 적만 때린다.
	public sealed class SummonFollowBehavior : IAiBehavior
	{
		private const float DecisionInterval = 0.25f;

		// 주인과 이 거리 안이면 멈춘다. 정확히 FollowDistance 에 맞추면 붙었다 떨어졌다 떨린다.
		private const float ArriveRatio = 0.8f;

		private float _decisionAccum = Random.Range(0f, DecisionInterval);

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			UnitBase owner = bb.SummonOwner;
			if (owner == null)
			{
				self.Mover.Stop();
				return;
			}

			_decisionAccum += dt;
			bool decide = _decisionAccum >= DecisionInterval;
			if (decide == true)
			{
				_decisionAccum -= DecisionInterval;
				MonsterAiCommon.AcquireTarget(self, bb);
			}

			UnitBase target = bb.Target;
			float attackRange = SummonAiCommon.GetAttackRange(self);

			// 사거리 안에 적이 있으면 멈춰 공격한다.
			if (target != null && SummonAiCommon.IsWithin(self, target, attackRange) == true)
			{
				self.Mover.Stop();
				self.Mover.SetFacing(target.CachedPos - self.CachedPos);
				if (decide == true)
				{
					SkillSelector.Select(self, false);
				}

				return;
			}

			// 그 외에는 주인을 따라간다.
			Vector2 toOwner = owner.CachedPos - self.CachedPos;
			float follow = bb.FollowDistance * ArriveRatio;
			if (toOwner.sqrMagnitude <= follow * follow)
			{
				self.Mover.Stop();
				return;
			}

			self.Mover.Move(toOwner.normalized, self.MoveSpeed);
		}
	}
}
