using UnityEngine;
using ProjectOne.Unit;

namespace ProjectOne.Projectile
{
	// 포물선 이동 궤적 — 논리(충돌) 위치는 발사 방향으로 직진하고, 스프라이트만 시각적으로 호를 그린다.
	// 진행도 t(0~1)에 따라 _visual 의 로컬 Y 를 sin 곡선으로 올렸다 내린다(콜라이더는 root 라 영향 없음).
	// 호의 기준 구간은 발사 시점 시작→타겟 거리(_arcSpan). 타겟이 없으면 사거리/수명으로 폴백한다.
	//
	// 탑다운에서 높이를 읽히게 하는 단서 세 가지:
	// - 타겟이 있으면 비행 시간(_flightTime)을 고정한다 — 거리와 무관하게 던지는 리듬이 같다
	// - 호 높이는 거리에 비례하되 _arcHeight 를 넘지 않는다 — 가까운 적에게 지나치게 높이 뜨지 않는다
	// - 바닥의 그림자(_shadow)는 높이 뜰수록 작고 옅어진다
	public class Trajectory_Parabolic : TrajectoryBase
	{
		// 호를 그릴 스프라이트 자식. 미지정이면 시각 오프셋을 생략(직진만).
		[SerializeField] private Transform _visual;

		// 호의 최고 높이 상한(로컬 단위).
		[SerializeField] private float _arcHeight = 1.2f;

		// 거리 1 당 호 높이. 실제 높이 = 거리 × 이 값, 단 _arcHeight 이하.
		[SerializeField] private float _arcHeightPerDistance = 0.3f;

		// 타겟이 있을 때 비행 시간(초). 0 이하면 프리펩 속도를 그대로 쓴다.
		[SerializeField] private float _flightTime = 0.5f;

		// 바닥 그림자. 미지정이면 그림자 연출을 생략한다.
		[SerializeField] private SpriteRenderer _shadow;

		// 최고점에서 그림자의 크기·불투명도 비율 (바닥일 때 1).
		[SerializeField] private float _shadowMinScale = 0.55f;
		[SerializeField] private float _shadowMinAlpha = 0.45f;

		private float _traveled;
		private float _elapsed;
		// 호 전체 구간 거리 — 발사 시점 시작→타겟 거리. 0이면 타겟 없음(사거리/수명으로 폴백).
		private float _arcSpan;
		// 이번 비행의 호 최고 높이
		private float _peak;

		private bool _hasShadowBase;
		private Vector3 _shadowBaseScale;
		private Color _shadowBaseColor;

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

			// 비행 시간 고정 — 거리만큼 속도를 맞춘다
			if (_flightTime > 0f && _arcSpan > 1E-04f)
			{
				_speed = _arcSpan / _flightTime;
			}

			_peak = _arcHeight;
			if (_arcSpan > 1E-04f)
			{
				_peak = Mathf.Min(_arcSpan * _arcHeightPerDistance, _arcHeight);
			}

			// 풀에서 재사용되므로 그림자 원본값은 처음 한 번만 잡는다
			if (_shadow != null && _hasShadowBase == false)
			{
				_shadowBaseScale = _shadow.transform.localScale;
				_shadowBaseColor = _shadow.color;
				_hasShadowBase = true;
			}

			applyHeight(0f);

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
				applyHeight(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)));
			}

			// 타겟 지점 도달 — Projectile 이 IsFinished 를 받아 소멸 처리(범위공격이면 그 지점에 피격판정)
			if (reachedTarget == true)
			{
				_isFinished = true;
			}

			return step;
		}

		// lift: 0(바닥) ~ 1(최고점)
		private void applyHeight(float lift)
		{
			if (_visual != null)
			{
				Vector3 local = _visual.localPosition;
				local.y = lift * _peak;
				_visual.localPosition = local;
			}

			if (_shadow != null && _hasShadowBase == true)
			{
				// 높이는 비행마다 다르므로 상한(_arcHeight) 대비 실제 높이로 줄인다 — 낮게 던지면 그림자도 덜 변한다
				float k = (_arcHeight > 1E-04f) ? lift * _peak / _arcHeight : 0f;
				_shadow.transform.localScale = _shadowBaseScale * Mathf.Lerp(1f, _shadowMinScale, k);
				Color c = _shadowBaseColor;
				c.a = _shadowBaseColor.a * Mathf.Lerp(1f, _shadowMinAlpha, k);
				_shadow.color = c;
			}
		}
	}
}
