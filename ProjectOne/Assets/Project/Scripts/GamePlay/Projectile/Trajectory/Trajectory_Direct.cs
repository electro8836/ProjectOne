using UnityEngine;

namespace ProjectOne.Projectile
{
	// 직선 이동 궤적 — 발사 방향으로 일정 속도 직진. 종료는 수명/사거리(Projectile)가 담당.
	public class Trajectory_Direct : TrajectoryBase
	{
		public override float Tick(float deltaTime, ref Vector3 position, out Vector2 facing)
		{
			float step = _speed * deltaTime;
			position += new Vector3(_direction.x, _direction.y, 0f) * step;
			facing = _direction;
			return step;
		}
	}
}
