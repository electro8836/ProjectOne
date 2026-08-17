using UnityEngine;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 고정형 몬스터 — 절대 움직이지 않는다. 사거리 안에 들어온 대상만 공격한다.
	//
	// 포탑·석상 같은 배치물이다. 리쉬 판정이 필요 없다(애초에 자리를 뜨지 않는다).
	public sealed class MonsterStationaryBehavior : IAiBehavior
	{
		private const float DecisionInterval = 0.25f;

		private float _decisionAccum = Random.Range(0f, DecisionInterval);

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			self.Mover.Stop();

			_decisionAccum += dt;
			if (_decisionAccum < DecisionInterval)
			{
				return;
			}

			_decisionAccum -= DecisionInterval;

			UnitBase target = MonsterAiCommon.AcquireTarget(self, bb);
			if (target == null)
			{
				return;
			}

			// Sector/Line 스킬이 조준되도록 시선만 돌린다.
			self.Mover.SetFacing(target.CachedPos - self.CachedPos);

			// 사거리 판정은 SkillSelector 가 스킬별로 한다 — 여기서 중복 계산하지 않는다.
			SkillSelector.Select(self, false);
		}
	}
}
