using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Projectile
{
	// 포물선 이동 궤적 — 논리(충돌) 위치는 발사 방향으로 직진하고, 스프라이트만 시각적으로 호를 그린다.
	// 진행도 t(0~1)에 따라 _visual 의 로컬 Y 를 sin 곡선으로 올렸다 내린다(콜라이더는 root 라 영향 없음).
	// 호의 기준 구간은 발사 시점 시작→타겟 거리(_arcSpan). 타겟이 없으면 사거리/수명으로 폴백한다.
	public class Trajectory_Parabolic : TrajectoryBase
	{
		// 호를 그릴 스프라이트 자식. 미지정이면 시각 오프셋을 생략(직진만).
		[SerializeField] private Transform _visual;

		// 호의 최고 높이(로컬 단위).
		[SerializeField] private float _arcHeight = 1f;

		private float _traveled;
		private float _elapsed;
		// 호 전체 구간 거리 — 발사 시점 시작→타겟 거리. 0이면 타겟 없음(사거리/수명으로 폴백).
		private float _arcSpan;

		public override void OnLaunch(in ProjectileData data, float speed, float maxDistance, float lifeTime)
		{
			base.OnLaunch(data, speed, maxDistance, lifeTime);
			_traveled = 0f;
			_elapsed = 0f;

			_arcSpan = 0f;
			if (data.target != null)
			{
				_arcSpan = Vector2.Distance(data.startPos, data.target.HitCenter);
			}

			// 호 기준이 전혀 없으면(타겟·사거리·수명 모두 없음) 호를 그릴 수 없음을 알린다.
			if (_visual != null && _arcSpan <= 1E-04f && _maxDistance <= 0f && _lifeTime <= 0f)
			{
				Debug.LogWarning($"[Trajectory_Parabolic] 호 기준 없음(타겟/사거리/수명 모두 0) — 직선 비행: {name}");
			}
		}

		public override float Tick(float deltaTime, ref Vector3 position, out Vector2 facing)
		{
			float step = _speed * deltaTime;

			// 발사 시 잡은 타겟 지점(_arcSpan)에 도달하면 그 지점에서 소멸 — 남은 거리만 이동 후 종료(지나치지 않음)
			bool reachedTarget = false;
			if (_arcSpan > 1E-04f)
			{
				float remaining = _arcSpan - _traveled;
				if (step >= remaining)
				{
					step = Mathf.Max(remaining, 0f);
					reachedTarget = true;
				}
			}

			position += new Vector3(_direction.x, _direction.y, 0f) * step;

			// 포물선은 회전하지 않는 고정 자세 — facing 0 을 반환해 Projectile 의 루트 회전을 건너뛴다.
			// (루트가 회전하면 자식 _visual/shadow 가 회전을 상속받아 호가 비스듬해지고 shadow 가 돌아간다)
			facing = Vector2.zero;

			_traveled += step;
			_elapsed += deltaTime;

			if (_visual != null)
			{
				// 진행도 — 타겟 거리 우선, 없으면 사거리, 없으면 수명 기준. 셋 다 없으면 호 생략.
				float t = -1f;
				if (_arcSpan > 1E-04f)
				{
					t = _traveled / _arcSpan;
				}
				else if (_maxDistance > 0f)
				{
					t = _traveled / _maxDistance;
				}
				else if (_lifeTime > 0f)
				{
					t = _elapsed / _lifeTime;
				}

				if (t >= 0f)
				{
					float height = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * _arcHeight;
					Vector3 local = _visual.localPosition;
					local.y = height;
					_visual.localPosition = local;
				}
			}

			// 타겟 지점 도달 — Projectile 이 IsFinished 를 받아 소멸 처리(범위공격이면 그 지점에 피격판정)
			if (reachedTarget == true)
			{
				_isFinished = true;
			}

			return step;
		}
	}
}
