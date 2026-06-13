using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Projectile
{
	// 유도 이동 궤적 — 타겟을 향해 매 프레임 _turnRate 만큼 선회하며 추적한다.
	// 타겟이 사라지거나 죽으면 마지막으로 본 위치까지 직진한 뒤 종료(IsFinished)한다.
	// 비행 중 충돌 판정은 타겟만 대상으로 한다(판정은 Projectile 이 Target/IsTargetLost 로 분기).
	public class Trajectory_Homing : TrajectoryBase
	{
		// 초당 선회 각(도). 클수록 빠르게 방향을 꺾어 추적한다.
		[SerializeField] private float _turnRate = 360f;

		private UnitBase _target;
		private Vector2 _lastTargetPos;
		private bool _isTargetLost;

		// 비행 중 충돌 판정 대상(타겟). 타겟 소실 비행 중에는 충돌을 무시해야 하므로 별도 플래그로 노출.
		public UnitBase Target
		{
			get { return _target; }
		}

		public bool IsTargetLost
		{
			get { return _isTargetLost; }
		}

		public override void OnLaunch(in ProjectileData data, float speed, float maxDistance, float lifeTime)
		{
			base.OnLaunch(data, speed, maxDistance, lifeTime);
			_target = data.target;
			_isTargetLost = false;
			_lastTargetPos = (_target != null) ? (Vector2)_target.HitCenter : Vector2.zero;
		}

		public override float Tick(float deltaTime, ref Vector3 position, out Vector2 facing)
		{
			Vector2 current = new Vector2(position.x, position.y);

			// 타겟 생존 여부 갱신 — null/사망이면 소실 처리(마지막 위치 고정)
			if (_isTargetLost == false)
			{
				if (_target == null || _target.IsDead == true)
				{
					_isTargetLost = true;
				}
				else
				{
					_lastTargetPos = _target.HitCenter;
				}
			}

			float step = _speed * deltaTime;

			// 소실 비행: 마지막 위치까지 직진, 도달하면 스냅 후 종료
			if (_isTargetLost == true)
			{
				Vector2 toLast = _lastTargetPos - current;
				float dist = toLast.magnitude;
				if (dist <= step || dist <= 1E-04f)
				{
					position = new Vector3(_lastTargetPos.x, _lastTargetPos.y, position.z);
					facing = (dist > 1E-04f) ? (toLast / dist) : _direction;
					_isFinished = true;
					return dist;
				}

				_direction = toLast / dist;
				position += new Vector3(_direction.x, _direction.y, 0f) * step;
				facing = _direction;
				return step;
			}

			// 추적: 진행 방향을 타겟 방향으로 _turnRate 만큼 선회 후 전진
			Vector2 toTarget = _lastTargetPos - current;
			if (toTarget.sqrMagnitude > 1E-06f)
			{
				float maxRadians = _turnRate * Mathf.Deg2Rad * deltaTime;
				_direction = Vector3.RotateTowards(_direction, toTarget.normalized, maxRadians, 0f);
				_direction.Normalize();
			}

			position += new Vector3(_direction.x, _direction.y, 0f) * step;
			facing = _direction;
			return step;
		}
	}
}
