using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Unit.AI
{
	// 몬스터 AI 3종(Melee / Ranged / Stationary)이 공유하는 판정.
	//
	// 탐지와 복귀는 AIType 과 무관하게 같은 규칙이고, 다른 것은 "타겟을 잡은 뒤 어떻게 움직이는가"뿐이다.
	// 그 부분만 각 behavior 가 구현한다.
	public static class MonsterAiCommon
	{
		// 앵커로 복귀했다고 보는 거리 — 이보다 가까워지면 리쉬 상태를 푼다.
		private const float ReturnArriveDistance = 0.5f;

		// 타겟 획득 (몬스터 설계 5장).
		//
		// - `Neutral` 은 피격으로 각성하기 전까지 아무도 잡지 않는다
		// - `Aggressive` 는 DetectRange 안의 최근접 히어로만 잡는다. 예전처럼 맵 끝까지 보지 않는다
		// - 이미 잡은 타겟은 죽거나 리쉬로 풀릴 때까지 유지한다(추격 중 매 판단마다 갈아타지 않도록)
		public static UnitBase AcquireTarget(UnitBase self, Blackboard bb)
		{
			if (bb.Target != null && bb.Target.IsDead == false)
			{
				return bb.Target;
			}

			bb.Target = null;
			if (bb.CanAcquireTarget == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			float detectSqr = bb.DetectRange * bb.DetectRange;
			float bestSqr = float.MaxValue;
			UnitBase best = null;

			Vector2 selfPos = self.CachedPos;
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero == null || hero.IsDead == true)
				{
					continue;
				}

				if (Skill.TargetResolver.IsEnemy(self.Faction, hero.Faction) == false)
				{
					continue;
				}

				float sqr = (hero.CachedPos - selfPos).sqrMagnitude;
				if (sqr > detectSqr || sqr >= bestSqr)
				{
					continue;
				}

				bestSqr = sqr;
				best = hero;
			}

			bb.Target = best;
			return best;
		}

		// 리쉬 판정 (몬스터 설계 5장).
		//
		// "플레이어와 얼마나 멀어지면 포기"가 아니라 **"자기 자리에서 얼마나 벗어나면 복귀"** 다.
		// 전자로 두면 플레이어가 몬스터를 맵 끝까지 끌고 다닐 수 있다.
		//
		// 복귀를 시작했거나 진행 중이면 true — 호출자는 이동만 처리하고 전투 로직을 건너뛴다.
		public static bool TickLeash(UnitBase self, Blackboard bb)
		{
			Vector2 selfPos = self.CachedPos;
			Vector2 toAnchor = bb.Anchor - selfPos;
			float distSqr = toAnchor.sqrMagnitude;

			if (bb.Returning == true)
			{
				if (distSqr <= ReturnArriveDistance * ReturnArriveDistance)
				{
					// 제자리 도착 — 각성 상태도 함께 푼다. 비선공은 다시 잠든다.
					bb.Returning = false;
					bb.Provoked = false;
					self.Mover.Stop();
					return false;
				}

				self.Mover.Move(toAnchor.normalized, self.MoveSpeed);
				return true;
			}

			float leashSqr = bb.LeashRange * bb.LeashRange;
			if (distSqr <= leashSqr)
			{
				return false;
			}

			bb.Returning = true;
			bb.Target = null;
			self.Mover.Move(toAnchor.normalized, self.MoveSpeed);
			return true;
		}
	}
}
