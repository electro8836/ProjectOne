using UnityEngine;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 추격형 소환물 — 적을 쫓아간다. 주인에게서 ReturnDistance 이상 벌어지면 복귀한다 (설계 7.5).
	//
	// 리쉬는 몬스터와 같은 장치를 쓴다. 다른 점은 앵커가 고정 좌표가 아니라 주인이라는 것뿐이라,
	// 매 틱 앵커를 주인 위치로 갱신하고 MonsterAiCommon.TickLeash 를 그대로 부른다.
	public sealed class SummonChaseBehavior : IAiBehavior
	{
		private const float DecisionInterval = 0.25f;

		private float _decisionAccum = Random.Range(0f, DecisionInterval);

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			if (bb.SummonOwner == null)
			{
				self.Mover.Stop();
				return;
			}

			SummonAiCommon.SyncAnchorToOwner(bb);

			if (MonsterAiCommon.TickLeash(self, bb) == true)
			{
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
			if (target == null)
			{
				self.Mover.Stop();
				return;
			}

			if (SummonAiCommon.IsWithin(self, target, SummonAiCommon.GetAttackRange(self)) == true)
			{
				self.Mover.Stop();
				self.Mover.SetFacing(target.CachedPos - self.CachedPos);
				if (decide == true)
				{
					SkillSelector.Select(self, false);
				}

				return;
			}

			Vector2 toTarget = target.CachedPos - self.CachedPos;
			self.Mover.Move(toTarget.normalized, self.MoveSpeed);
		}
	}
}
