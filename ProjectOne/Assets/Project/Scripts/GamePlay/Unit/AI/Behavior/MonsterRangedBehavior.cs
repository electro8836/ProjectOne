using UnityEngine;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 원거리 몬스터 — **사거리 안이면 그 자리에서 쏘고, 밖이면 접근한다.** 그게 전부다 (몬스터 설계 5장).
	//
	// `KeepDistance`(플레이어가 붙으면 물러남)를 두지 않는 이유는 설계가 명시적으로 폐기했기 때문이다.
	// 잡기만 힘들어지고 몬스터가 흩어져 광역 스킬 효율이 떨어진다. 방치형이라 플레이어가 자동으로 붙는데
	// 몬스터가 흩어지면 DPS 체감이 나빠진다.
	//
	// 사거리는 Skill 테이블이 이미 갖고 있으므로 AI 테이블에 중복해 두지 않는다.
	public sealed class MonsterRangedBehavior : IAiBehavior
	{
		// 타겟/사거리 판단 주기. 이동은 매 프레임이라 반응성은 유지된다.
		private const float DecisionInterval = 0.25f;

		// 사거리를 못 구했을 때의 폴백
		private const float FallbackRange = 5f;

		// 사거리 안이면 정지, 사거리 × 이 값을 넘어야 다시 접근 — 경계 떨림 방지
		private const float Hysteresis = 1.1f;

		private float _decisionAccum = Random.Range(0f, DecisionInterval);
		private float _cachedRange = -1f;
		private bool _approaching = true;

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			if (MonsterAiCommon.TickLeash(self, bb) == true)
			{
				return;
			}

			_decisionAccum += dt;
			bool decide = _decisionAccum >= DecisionInterval;
			if (decide == true)
			{
				_decisionAccum -= DecisionInterval;
				decideState(self, bb);
			}

			UnitBase target = bb.Target;
			if (target == null)
			{
				self.Mover.Stop();
				return;
			}

			self.Mover.SetFacing(target.CachedPos - self.CachedPos);

			if (_approaching == false)
			{
				// 사거리 안 — 제자리에서 쏜다
				self.Mover.Stop();
				if (decide == true)
				{
					SkillSelector.Select(self, false);
				}

				return;
			}

			Vector2 dir = target.CachedPos - self.CachedPos;
			self.Mover.Move(dir + self.CachedSeparation, self.MoveSpeed);
		}

		private void decideState(UnitBase self, Blackboard bb)
		{
			UnitBase target = MonsterAiCommon.AcquireTarget(self, bb);
			if (target == null)
			{
				return;
			}

			if (_cachedRange < 0f)
			{
				float min = (self.SkillContainer != null) ? self.SkillContainer.GetMinSkillRange() : -1f;
				_cachedRange = (min > 0f) ? min : FallbackRange;
			}

			float distSqr = (target.CachedPos - self.CachedPos).sqrMagnitude;
			float inRange = _cachedRange;
			float outRange = _cachedRange * Hysteresis;

			if (_approaching == true)
			{
				if (distSqr <= inRange * inRange)
				{
					_approaching = false;
				}
			}
			else if (distSqr > outRange * outRange)
			{
				_approaching = true;
			}
		}
	}
}
