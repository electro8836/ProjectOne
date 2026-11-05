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

		// 선회 가속 — 발사 직후엔 _initialTurnRate 로 거의 곧게 날다가, _turnHoldTime 뒤부터
		// _turnRampTime 동안 _turnRate 까지 올라간다. 두 시간이 모두 0이면 처음부터 _turnRate 다.
		[SerializeField] private float _initialTurnRate = 60f;
		[SerializeField] private float _turnHoldTime = 0f;
		[SerializeField] private float _turnRampTime = 0f;

		// 근접 선회 강화 — 타겟과의 거리가 _closeRange 안으로 들어오면 가까울수록 _closeTurnRate 쪽으로 올린다.
		// 선회 반경 때문에 타겟 주위를 맴도는 것을 막는다. _closeRange 가 0이면 쓰지 않는다.
		[SerializeField] private float _closeRange = 0f;
		[SerializeField] private float _closeTurnRate = 1440f;

		// 발사 후 흐른 시간 — 선회 가속 계산용
		private float _elapsed;

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
			_elapsed = 0f;
			_lastTargetPos = (_target != null) ? (Vector2)_target.HitCenter : Vector2.zero;
		}

		// 튕김 — 새 타겟으로 바꾸고 진행 방향을 바로 그쪽으로 꺾는다(선회 반경 때문에 가까운 대상을 지나치지 않게).
		public void Retarget(UnitBase target, Vector2 from)
		{
			_target = target;
			_isTargetLost = false;
			_lastTargetPos = target.HitCenter;

			Vector2 toTarget = _lastTargetPos - from;
			if (toTarget.sqrMagnitude > 1E-06f)
			{
				_direction = toTarget.normalized;
			}
		}

		public override float Tick(float deltaTime, ref Vector3 position, out Vector2 facing)
		{
			Vector2 current = new Vector2(position.x, position.y);
			_elapsed += deltaTime;

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

			// 추적: 진행 방향을 타겟 방향으로 현재 선회 각만큼 선회 후 전진
			Vector2 toTarget = _lastTargetPos - current;
			if (toTarget.sqrMagnitude > 1E-06f)
			{
				float maxRadians = currentTurnRate(toTarget.magnitude) * Mathf.Deg2Rad * deltaTime;
				_direction = Vector3.RotateTowards(_direction, toTarget.normalized, maxRadians, 0f);
				_direction.Normalize();
			}

			position += new Vector3(_direction.x, _direction.y, 0f) * step;
			facing = _direction;
			return step;
		}

		// 선회 각(도/초) — 발사 후 경과 시간으로 정하고, 타겟에 가까우면 근접 선회 강화로 끌어올린다.
		private float currentTurnRate(float distance)
		{
			float rate = timedTurnRate();
			if (_closeRange <= 0f || distance >= _closeRange || _closeTurnRate <= rate)
			{
				return rate;
			}

			float t = 1f - distance / _closeRange;
			return Mathf.Lerp(rate, _closeTurnRate, t);
		}

		// 발사 후 경과 시간에 따른 선회 각(도/초)
		private float timedTurnRate()
		{
			if (_turnHoldTime <= 0f && _turnRampTime <= 0f)
			{
				return _turnRate;
			}

			if (_elapsed <= _turnHoldTime)
			{
				return _initialTurnRate;
			}

			if (_turnRampTime <= 0f)
			{
				return _turnRate;
			}

			float t = Mathf.Clamp01((_elapsed - _turnHoldTime) / _turnRampTime);
			return Mathf.Lerp(_initialTurnRate, _turnRate, t);
		}
	}
}
