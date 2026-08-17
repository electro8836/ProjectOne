using UnityEngine;
using ProjectOne.Skill;
using ProjectOne.Unit.AI.BT;

namespace ProjectOne.Unit.AI
{
	// 보스 — BT 로 동작한다 (몬스터 설계 5장: AIType 이 FSM/BT 선택을 겸하며 Boss 만 BT).
	//
	// **패턴 데이터가 아직 없다.** `Monster.SkillIDs` 가 전부 비어 있어 지금 트리는
	// "리쉬 → 타겟 없으면 대기 → 사거리 밖이면 접근 → 안이면 시전" 한 갈래뿐이다.
	// 패턴이 생기면 이 트리에 `BTSequence` 를 추가하는 형태로 확장한다 —
	// 스킬 ID 를 코드에 박지 않고 `SkillIDs` 를 참조하므로 BT 를 열어볼 필요가 없다 (설계 3장).
	public sealed class BossBehavior : IAiBehavior
	{
		private const float DecisionInterval = 0.2f;
		private const float FallbackRange = 2f;

		private readonly IBTNode _root;

		private float _decisionAccum = Random.Range(0f, DecisionInterval);
		private float _cachedRange = -1f;

		public BossBehavior()
		{
			_root = new BTSelector(
				// [1] 자리에서 너무 벗어났으면 전투를 접고 복귀한다
				new BTAction(tickLeash),

				// [2] 타겟이 없으면 멈춰 선다
				new BTSequence(
					new BTCondition(hasNoTarget),
					new BTAction(idle)),

				// [3] 사거리 밖이면 접근
				new BTSequence(
					new BTCondition(isOutOfRange),
					new BTAction(approach)),

				// [4] 사거리 안이면 시전
				new BTAction(attack));
		}

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			// 타겟 탐색만 주기적으로 — 트리 자체는 매 프레임 돈다(이동 반응성 유지).
			_decisionAccum += dt;
			if (_decisionAccum >= DecisionInterval)
			{
				_decisionAccum -= DecisionInterval;
				MonsterAiCommon.AcquireTarget(self, bb);
			}

			BTContext ctx = new BTContext(self, bb, dt);
			_root.Tick(in ctx);
		}

		// ── 노드 구현 ─────────────────────────────────────────────────

		private BTStatus tickLeash(in BTContext ctx)
		{
			return MonsterAiCommon.TickLeash(ctx.Self, ctx.Blackboard) ? BTStatus.Running : BTStatus.Failure;
		}

		private bool hasNoTarget(in BTContext ctx)
		{
			return ctx.Blackboard.Target == null;
		}

		private BTStatus idle(in BTContext ctx)
		{
			ctx.Self.Mover.Stop();
			return BTStatus.Running;
		}

		private bool isOutOfRange(in BTContext ctx)
		{
			UnitBase target = ctx.Blackboard.Target;
			if (target == null)
			{
				return false;
			}

			float range = getRange(ctx.Self);
			float stopDist = Mathf.Max(range, ctx.Self.Radius + target.Radius);
			return (target.CachedPos - ctx.Self.CachedPos).sqrMagnitude > stopDist * stopDist;
		}

		private BTStatus approach(in BTContext ctx)
		{
			UnitBase target = ctx.Blackboard.Target;
			Vector2 dir = target.CachedPos - ctx.Self.CachedPos;
			ctx.Self.Mover.SetFacing(dir);
			ctx.Self.Mover.Move(dir + ctx.Self.CachedSeparation, ctx.Self.MoveSpeed);
			return BTStatus.Running;
		}

		private BTStatus attack(in BTContext ctx)
		{
			UnitBase target = ctx.Blackboard.Target;
			if (target == null)
			{
				return BTStatus.Failure;
			}

			ctx.Self.Mover.Stop();
			ctx.Self.Mover.SetFacing(target.CachedPos - ctx.Self.CachedPos);

			// 쿨이 돈 액티브가 있으면 그것을, 없으면 기본공격을 쓴다 (설계 3장).
			SkillSelector.Select(ctx.Self, false);
			return BTStatus.Running;
		}

		private float getRange(UnitBase self)
		{
			if (_cachedRange < 0f)
			{
				float min = (self.SkillContainer != null) ? self.SkillContainer.GetMinSkillRange() : -1f;
				_cachedRange = (min > 0f) ? min : FallbackRange;
			}

			return _cachedRange;
		}
	}
}
