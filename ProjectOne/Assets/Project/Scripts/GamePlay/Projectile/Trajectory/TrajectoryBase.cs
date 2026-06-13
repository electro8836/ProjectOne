using UnityEngine;

namespace ProjectOne.Projectile
{
	// 궤적 컴포넌트 공통 베이스 — 방향/속도/사거리/수명 등 공통 컨텍스트와 종료 플래그를 보관한다.
	// 실제 이동 산출은 파생 클래스의 Tick 이 담당한다.
	public abstract class TrajectoryBase : MonoBehaviour, ITrajectory
	{
		// OnLaunch 로 주입받는 런타임 컨텍스트 (발사체 프리팹이 소유한 속도/사거리/수명 포함)
		protected Vector2 _direction;
		protected float _speed;
		protected float _maxDistance;
		protected float _lifeTime;

		protected bool _isFinished;

		public bool IsFinished
		{
			get { return _isFinished; }
		}

		public virtual void OnLaunch(in ProjectileData data, float speed, float maxDistance, float lifeTime)
		{
			Vector2 dir = new Vector2(data.direction.x, data.direction.y);
			_direction = (dir.sqrMagnitude > 1E-06f) ? dir.normalized : Vector2.right;
			_speed = speed;
			_maxDistance = maxDistance;
			_lifeTime = lifeTime;
			_isFinished = false;
		}

		public abstract float Tick(float deltaTime, ref Vector3 position, out Vector2 facing);
	}
}
