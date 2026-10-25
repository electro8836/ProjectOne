using UnityEngine;
using ProjectOne.Combat;

namespace ProjectOne.Unit
{
	// 마을 데미지 측정용 더미 — 맞으면 데미지 텍스트와 피격 모션만 내고 체력은 깎이지 않는다(죽지 않는다).
	//
	// 유닛 종류는 Monster 로 보고한다. 히어로 AI 가 Monster 만 노리고, 데미지 텍스트도 몬스터 피격 모양으로 뜬다.
	// Monster 클래스를 쓰지 않으므로 처치 이벤트(보상·퀘스트·드랍)가 나가지 않는다.
	// 스탯은 전부 0(방어·회피·피해감소 없음)이라 공격자의 원래 데미지가 그대로 찍힌다.
	// 디버프 모션은 UnitBase.ManualTick 이 버프 상태로 켜고 끈다.
	[RequireComponent(typeof(UnitMover), typeof(UnitAnimator))]
	public class TrainingDummy : UnitBase, IDamageable
	{
		public override UnitType GetUnitType()
		{
			return UnitType.Monster;
		}

		public void TakeDamage(in DamageInfo info)
		{
			if (_animator != null)
			{
				_animator.TriggerHitFlash();
			}

			HandleHit(in info);

			// 제자리 고정 — 넉백 충격을 바로 지운다
			if (_mover != null)
			{
				_mover.ClearImpulse();
			}

			// 일반 피격도 매번 Damaged 모션을 낸다 (몬스터는 경직·기절에만 낸다)
			PlayStaggerMotion();
		}

		public override void ManualTick(float dt)
		{
			base.ManualTick(dt);

			if (_animator != null)
			{
				_animator.TickHitFlash(dt);
			}
		}

		void IDamageable.TakeDamage(in DamageInfo info)
		{
			TakeDamage(in info);
		}
	}
}
