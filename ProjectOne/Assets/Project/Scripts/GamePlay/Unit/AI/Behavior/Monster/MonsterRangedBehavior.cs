using UnityEngine;
using EDT;
using ProjectOne.Map;
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
	//
	// 접근은 접근형과 같이 플로우필드를 따른다 — 직선으로 밀면 구덩이/벽에 막혀 벽면을 따라 미끄러지기만 한다.
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

		// 의사결정에서 산출한 접근 방향 — 매 프레임 이동이 이 값 + 최신 분리벡터로 조향한다
		private Vector2 _cachedApproachDir;

		// 평타가 발사체인지 — 불변이라 _cachedRange 와 함께 최초 1회만 조회
		private bool _basicIsProjectile;

		// 시야(LoS) 캐시 — 계산은 decideState 주기로만 하고, 매 프레임 정지 판정은 이 값을 읽는다
		private bool _hasClearShot = true;

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			// 스킬/평타 모션이 도는 동안은 그 자리에서 마친다 — 이동도 판단도 하지 않는다
			SkillContainer sc = self.SkillContainer;
			if (sc != null && sc.IsInAction == true)
			{
				self.Mover.Stop();
				return;
			}

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

			// 정지/재접근은 매 프레임 판정한다 — 판단 주기(0.25초)에 묶어 두면 사거리에 들고도
			// 그 시간만큼 더 걸어 들어가 찔끔찔끔 떨림이 된다.
			updateApproaching(self, target);

			// 사거리에 든 스킬이 있으면 접근 도중에도 쏜다 — 정지 거리는 평타 기준이라
			// 사거리가 긴 스킬을 정지할 때까지 묵혀 두면 안 된다.
			if (decide == true)
			{
				SkillSelector.Select(self, false);
			}

			if (_approaching == false)
			{
				// 사거리 안 — 제자리에서 쏜다
				self.Mover.Stop();
				return;
			}

			self.Mover.Move(_cachedApproachDir + self.CachedSeparation, self.MoveSpeed);
		}

		// 정지/재접근 밴드 판정 — 매 프레임 호출된다. 비싼 시야 판정은 decideState 가 갱신한 _hasClearShot 을 쓴다.
		// 정지 거리는 스킬 발동 조건(TargetResolver: ScanRange + 타겟 반지름)과 같은 기준으로 맞춘다 —
		// 기준이 어긋나면 사거리에서 시전해 놓고 시전이 끝난 뒤 그 차이만큼 더 걸어 들어간다.
		private void updateApproaching(UnitBase self, UnitBase target)
		{
			if (_cachedRange < 0f)
			{
				return;
			}

			float distSqr = (target.CachedPos - self.CachedPos).sqrMagnitude;
			float stopDist = _cachedRange + target.Radius;

			if (_approaching == true)
			{
				if (distSqr <= stopDist * stopDist && _hasClearShot == true)
				{
					_approaching = false;
				}

				return;
			}

			float outRange = stopDist * Hysteresis;
			if (distSqr > outRange * outRange || _hasClearShot == false)
			{
				_approaching = true;
			}
		}

		private void decideState(UnitBase self, Blackboard bb)
		{
			UnitBase target = MonsterAiCommon.AcquireTarget(self, bb);
			if (target == null)
			{
				return;
			}

			// 정지 사거리·발사체여부는 불변 — 최초 1회만 구한다
			if (_cachedRange < 0f)
			{
				_cachedRange = getStoppingRange(self);
				Table_Skill.Row basicRow = getBasicAttackRow(self);
				_basicIsProjectile = (basicRow != null && SkillSelector.IsProjectileSkill(basicRow) == true);
			}

			Vector2 selfPos = self.CachedPos;
			Vector2 dirToTarget = target.CachedPos - selfPos;

			// LoS 는 비싸므로 이 주기에만 갱신한다. 구덩이/벽 건너에서 사거리에만 들었다고 멈추면
			// 발사체가 나가지 않아(SkillSelector 가 시야를 본다) 쏘지도 못한 채 서 있게 된다.
			_hasClearShot = hasClearShot(self, target);

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

			_cachedApproachDir = approach;
		}

		// 정지 사거리 — 평타 사거리. 평타가 없는 캐스터 전용 몬스터는 보유 스킬 최소 사거리로 폴백한다.
		private static float getStoppingRange(UnitBase self)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return FallbackRange;
			}

			float range = sc.GetBasicAttackRange();
			if (range > 0f)
			{
				return range;
			}

			range = sc.GetMinSkillRange();
			return (range > 0f) ? range : FallbackRange;
		}

		// 기본공격 스킬 행 — 없으면 null
		private static Table_Skill.Row getBasicAttackRow(UnitBase self)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return null;
			}

			EDT.Skill basic = sc.GetBasicAttack();
			if (basic == EDT.Skill.None)
			{
				return null;
			}

			ProjectOne.Skill.ResolvedSkill resolved = self.Resolve(basic);
			return (resolved != null) ? resolved.Row : null;
		}

		// 발사체 평타일 때만 시야(LoS)를 따진다 — 근접/비발사체나 맵 없음이면 항상 사격 가능으로 본다.
		private bool hasClearShot(UnitBase self, UnitBase target)
		{
			if (_basicIsProjectile == false)
			{
				return true;
			}

			if (MapManager.HasInstance == false)
			{
				return true;
			}

			return MapManager.Instance.HasLineOfSight(self.HitCenter, target.HitCenter);
		}
	}
}
