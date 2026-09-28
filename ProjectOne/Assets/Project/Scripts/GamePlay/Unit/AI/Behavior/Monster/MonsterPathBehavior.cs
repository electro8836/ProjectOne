using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Unit.AI
{
	// 경로 이동형 — 웨이포인트를 순서대로 밟아 도착지점까지 간다. 공격하지 않는다.
	//
	// 균열 던전이 스폰마다 AiBrain.SetBehaviorOverride 로 덮어쓴다. 원래 AI(Monster.AIType)는 풀에 그대로
	// 남아 있고, 다음 스폰의 ResetForSpawn 이 덮어쓰기를 걷는다.
	//
	// 이웃 분리 벡터를 싣지 않는다 — 경로가 좁은 통로라 옆으로 밀리면 벽에 걸린다. 겹쳐 지나가는 쪽이 낫다.
	public sealed class MonsterPathBehavior : IAiBehavior
	{
		// 도착 판정 최소 거리. 이동량이 이보다 크면 이동량을 쓴다(한 프레임에 지나치지 않도록).
		private const float ArriveDistance = 0.1f;

		private readonly IReadOnlyList<Vector3> _points;
		private readonly Action<Monster> _onArrived;

		private int _next;
		private bool _arrived;

		public MonsterPathBehavior(IReadOnlyList<Vector3> points, Action<Monster> onArrived)
		{
			_points = points;
			_onArrived = onArrived;
		}

		public void Tick(UnitBase self, Blackboard bb, float dt)
		{
			if (_arrived == true)
			{
				self.Mover.Stop();
				return;
			}

			if (_points == null || _next >= _points.Count)
			{
				arrive(self);
				return;
			}

			Vector2 target = _points[_next];
			Vector2 delta = target - self.CachedPos;
			float step = self.MoveSpeed * dt;
			float threshold = (step > ArriveDistance) ? step : ArriveDistance;

			if (delta.sqrMagnitude <= threshold * threshold)
			{
				_next++;
				if (_next >= _points.Count)
				{
					arrive(self);
				}

				return;
			}

			self.Mover.Move(delta, self.MoveSpeed);
		}

		private void arrive(UnitBase self)
		{
			_arrived = true;
			self.Mover.Stop();

			Monster monster = self as Monster;
			if (monster != null && _onArrived != null)
			{
				_onArrived.Invoke(monster);
			}
		}
	}
}
