using System.Collections.Generic;
using EDT;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 히어로 자동전투 전략 — 이동은 플레이어(HeroController)가, 조준과 시전은 AI 가 담당한다.
	//
	// 교전은 이동 여부와 무관하게 일어난다.
	//   사거리 안에 적 있음 : 시선 = 최근접 적, 공격
	//   사거리 안에 적 없음 : 시선 = 이동 방향 (정지 시 직전 이동 방향 유지)
	//
	// 조준은 이동 방향과 무관하다. UnitMover.Facing 은 이동 속도로 자동 갱신되므로
	// 그대로 두면 조이스틱 방향이 곧 공격 방향이 되어, 적을 때리려면 적 쪽으로 움직여야 한다.
	// SetFacing 으로 덮어써서 이동과 조준을 분리한다.
	//
	// 고유스킬(Special)은 HUD 버튼 수동이므로 castSpecial=false.
	public sealed class HeroAutoBehavior : IAiBehavior
	{
		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			if (self.Mover == null)
			{
				return;
			}

			aimAtNearest(self);
			SkillSelector.Select(self, false);
		}

		// 최근접 적 조준. Target 탐색은 facing 을 쓰지 않으므로 이동 방향과 무관하게 고른다.
		//
		// 조준 반경은 실제 공격 사거리와 같다 — 못 때리는 거리에서 미리 돌아볼 이유가 없다.
		// GetMinSkillRange 는 리졸브 결과의 ScanRange 를 보므로 "사거리 +20%" 옵션도 함께 반영된다.
		//
		// 타겟을 고정하지 않고 매 틱 다시 고른다 — 히어로는 몬스터와 달리 추격 일관성이 필요 없고,
		// 더 가까운 적이 붙으면 그쪽을 보는 편이 자연스럽다.
		//
		// 사거리 안에 적이 없으면 SetFacing 을 부르지 않는다 — 마지막 이동 방향이 그대로 유지된다.
		private static void aimAtNearest(UnitBase self)
		{
			if (self.SkillContainer == null)
			{
				return;
			}

			float range = self.SkillContainer.GetMinSkillRange();
			if (range <= 0f)
			{
				return;
			}

			List<UnitBase> found = TargetResolver.ScanByType(SkillScanTypes.Target, SkillApplyTarget.Enemy, range, 1f, self);
			if (found.Count == 0)
			{
				return;
			}

			UnitBase target = found[0];
			if (target == null)
			{
				return;
			}

			self.Mover.SetFacing(target.CachedPos - self.CachedPos);
		}
	}
}
