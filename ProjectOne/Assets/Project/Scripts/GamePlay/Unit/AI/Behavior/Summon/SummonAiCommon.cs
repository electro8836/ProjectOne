using UnityEngine;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 소환물 AI 3종(Follow / Chase / Wander)이 공유하는 판정.
	public static class SummonAiCommon
	{
		// 사거리를 못 구할 때의 폴백. 0으로 두면 영원히 붙지 못해 공격을 안 한다.
		private const float FallbackRange = 1f;

		// 소환물의 유효 공격 사거리 — 보유 스킬 중 최소 사거리.
		// 매 틱 조회해도 되는 이유: 소환물은 소수이고, ScanRange 가 소환 반경 상속으로 바뀔 수 있다.
		public static float GetAttackRange(UnitBase self)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return FallbackRange;
			}

			float min = sc.GetMinSkillRange();
			return (min > 0f) ? min : FallbackRange;
		}

		// 물리 접촉 거리와 공격 사거리 중 큰 쪽으로 판정한다 — 사거리가 작아도 닿으면 때린다.
		public static bool IsWithin(UnitBase self, UnitBase target, float range)
		{
			float stop = Mathf.Max(range, self.Radius + target.Radius);
			return (target.CachedPos - self.CachedPos).sqrMagnitude <= stop * stop;
		}

		// 소환물의 앵커는 고정 좌표가 아니라 주인이다. 매 틱 갱신해야 TickLeash 가 의미를 갖는다.
		public static void SyncAnchorToOwner(Blackboard bb)
		{
			if (bb.SummonOwner != null)
			{
				bb.Anchor = bb.SummonOwner.CachedPos;
			}
		}
	}
}
