using UnityEngine;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 보스 — 페이즈 스킬세트 구동.
	//
	// BT 를 쓰지 않는다. 원하는 동작이 "매 틱 우선순위 재평가"(BT 의 강점)가 아니라
	// "페이즈마다 스킬세트가 바뀌고 HP 조건에서 관문(전멸기)을 거는" 상태 진행이기 때문이다.
	//
	// 역할 분담
	//   BossPhaseRunner — 페이즈 판정, 전환 시퀀스(무적·전멸기·기믹), 현재 스킬세트 제공
	//   여기            — 이동/조준과 "지금 무엇을 쓸까"(SkillSelector.SelectFrom)
	//
	// 스킬 ID 를 코드에 박지 않는다 — 세트도 관문도 전부 테이블이다 (설계 3장).
	public sealed class BossBehavior : IAiBehavior, IAiSpawnReset
	{
		private const float DecisionInterval = 0.2f;
		private const float FallbackRange = 2f;

		private readonly BossPhaseRunner _runner = new BossPhaseRunner();

		private float _decisionAccum = Random.Range(0f, DecisionInterval);
		private float _cachedRange = -1f;

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			// 스킬/평타 모션이 도는 동안은 그 자리에서 마친다 — 이동도 판단도 하지 않는다
			SkillContainer sc = self.SkillContainer;
			if (sc != null && sc.IsInAction == true)
			{
				self.Mover.Stop();
				return;
			}

			// 페이즈 전환(무적 + 전멸기 캐스팅) 중에는 그 자리에 선다.
			// 캐스팅이 BlockMove 를 이미 걸지만, AI 가 조향을 시도하지 않게 명시적으로 멈춘다.
			if (_runner.Tick(self) == true)
			{
				self.Mover.Stop();
				return;
			}

			// 자리에서 너무 벗어났으면 전투를 접고 복귀한다
			if (MonsterAiCommon.TickLeash(self, bb) == true)
			{
				return;
			}

			// 타겟 탐색만 주기적으로 — 이동은 매 프레임이라 반응성은 유지된다.
			_decisionAccum += dt;
			if (_decisionAccum >= DecisionInterval)
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

			// 사거리 밖이면 접근 — 겹침은 분리 벡터로 푼다.
			Vector2 toTarget = target.CachedPos - self.CachedPos;
			float stopDist = Mathf.Max(getRange(self), self.Radius + target.Radius);
			if (toTarget.sqrMagnitude > stopDist * stopDist)
			{
				self.Mover.SetFacing(toTarget);
				self.Mover.Move(toTarget + self.CachedSeparation, self.MoveSpeed);
				return;
			}

			// 사거리 안 — 응시하고 현재 페이즈 세트에서 고른다.
			self.Mover.Stop();
			self.Mover.SetFacing(toTarget);

			if (_runner.HasPhases == true)
			{
				SkillSelector.SelectFrom(self, _runner.CurrentSkillSet);
				return;
			}

			// 페이즈 데이터가 없는 보스 — 보유 스킬 전체로 기존 규칙을 따른다.
			SkillSelector.Select(self, false);
		}

		// 스폰 리셋 — 풀에서 다시 꺼내 쓸 때 페이즈를 1부터 되돌린다.
		public void OnSpawnReset(UnitBase self)
		{
			_runner.ResetForSpawn(self);
		}

		// 정지 거리는 보유 스킬의 최소 사거리다. 불변이라 최초 1회만 조회한다.
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
