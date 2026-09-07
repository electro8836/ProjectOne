using UnityEngine;

namespace ProjectOne.Unit.AI
{
	// 배회형 소환물 — 주인 주변 FollowDistance 안을 어슬렁거린다. 전투에 참여하지 않는다 (설계 7.5).
	//
	// 연출용이므로 타겟 탐색도 스킬 시전도 하지 않는다. 가장 단순한 behavior 다.
	public sealed class SummonWanderBehavior : IAiBehavior
	{
		// 목적지를 새로 고르는 주기
		private const float RepickInterval = 2f;

		// 목적지에 닿았다고 보는 거리
		private const float ArriveDistance = 0.3f;

		private float _repickAccum = Random.Range(0f, RepickInterval);

		private Vector2 _destination;

		private bool _hasDestination;

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			UnitBase owner = bb.SummonOwner;
			if (owner == null)
			{
				self.Mover.Stop();
				return;
			}

			_repickAccum += dt;

			Vector2 selfPos = self.CachedPos;
			bool arrived = _hasDestination == true && (_destination - selfPos).sqrMagnitude <= ArriveDistance * ArriveDistance;

			if (_hasDestination == false || arrived == true || _repickAccum >= RepickInterval)
			{
				_repickAccum = 0f;
				_destination = owner.CachedPos + Random.insideUnitCircle * bb.FollowDistance;
				_hasDestination = true;
			}

			Vector2 toDest = _destination - selfPos;
			if (toDest.sqrMagnitude <= ArriveDistance * ArriveDistance)
			{
				self.Mover.Stop();
				return;
			}

			self.Mover.Move(toDest.normalized, self.MoveSpeed);
		}
	}
}
