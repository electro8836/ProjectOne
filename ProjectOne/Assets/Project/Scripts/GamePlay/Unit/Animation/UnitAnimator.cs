using UnityEngine;

namespace ProjectOne.Unit
{
	public class UnitAnimator : MonoBehaviour
	{
		private Animator _animator;

		private SpriteRenderer _spriteRenderer;

		private bool _lastIsMoving;

		// 좌우 플립 판정용 — facing.x 를 시간 기반으로 누적해 고빈도 부호 진동(분산 조향 떨림)을 흡수
		private float _facingXSmoothed;

		private const float FacingSmoothRate = 10f;

		private const float FacingDeadband = 0.05f;

		[SerializeField]
		private float _attackSpeedScale = 100f;

		[SerializeField]
		private float _moveSpeedScale = 100f;

		[SerializeField]
		private float _minMotionMul = 0.1f;

		[SerializeField]
		private float _maxMotionMul = 5f;

		// worldY → sortingOrder 변환 정밀도. sortingOrder는 int16(±32767)이므로
		// (맵 Y폭 × _precision ≤ 32767) 와 (구분할 최소 Y간격 × _precision ≥ 1) 사이에서 잡는다.
		[SerializeField]
		private float _precision = 100f;

		// 루트가 발밑이 아닐 때 정렬 기준점(발밑)을 맞추는 보정값
		[SerializeField]
		private float _yOffset = 0f;

		private int _lastSortOrder = int.MinValue;

		private float _lastAttackSpeedMul = 1f;

		private float _lastMoveSpeedMul = 1f;

		private static readonly int HashIsMoving = Animator.StringToHash("IsMoving");

		private static readonly int HashAttack = Animator.StringToHash("Attack");

		private static readonly int HashSkill = Animator.StringToHash("Skill");

		private static readonly int HashHit = Animator.StringToHash("Hit");

		private static readonly int HashHDead = Animator.StringToHash("Dead");

		private static readonly int HashIsDead = Animator.StringToHash("IsDead");

		private static readonly int HashAttackSpeedMul = Animator.StringToHash("AttackSpeedMul");

		private static readonly int HashMoveSpeedMul = Animator.StringToHash("MoveSpeedMul");

		private void Awake()
		{
			_animator = this.GetComponentInChildren<Animator>();
			_spriteRenderer = this.GetComponentInChildren<SpriteRenderer>();
		}

		// 발밑(피벗 하단) Y좌표를 정수 sortingOrder로 변환해 유닛 간 앞뒤 정렬을 결정한다.
		private void LateUpdate()
		{
			float sortY = transform.position.y + _yOffset;
			int order = -Mathf.RoundToInt(sortY * _precision); // Y가 클수록(위) 뒤로 → 음수
			if (_lastSortOrder != order)
			{
				_lastSortOrder = order;
				_spriteRenderer.sortingOrder = order;
			}
		}

		public void SetMoving(bool isMoving)
		{
			if (_lastIsMoving != isMoving)
			{
				_lastIsMoving = isMoving;
				_animator.SetBool(HashIsMoving, isMoving);
			}
		}

		public void SetFacing(Vector2 facing)
		{
			// 이동 벡터 x를 시간 기반으로 부드럽게 누적 — 평균 이동 방향은 일관되므로
			// 평활값 부호는 실제 진행 방향(우회 길찾기 포함)을 따라가되 순간 떨림만 흡수
			_facingXSmoothed = Mathf.Lerp(_facingXSmoothed, facing.x, Time.deltaTime * FacingSmoothRate);

			// 0 근처(거의 수직 이동)면 마지막 좌우 방향 유지 → 미세 부호반전 플립 방지
			if (_facingXSmoothed > FacingDeadband)
			{
				ApplyFlip(false);
			}
			else if (_facingXSmoothed < -FacingDeadband)
			{
				ApplyFlip(true);
			}
		}

		private void ApplyFlip(bool flip)
		{
			if (_spriteRenderer.flipX != flip)
			{
				_spriteRenderer.flipX = flip;
			}
		}

		public void PlayAttack()
		{
			_animator.SetTrigger(HashAttack);
		}

		public void PlaySkill()
		{
			_animator.SetTrigger(HashSkill);
		}

		public void PlayMotion(string motionName)
		{
			if (!string.IsNullOrEmpty(motionName))
			{
				_animator.SetTrigger(Animator.StringToHash(motionName));
			}
		}

		public void SetAttackSpeed(float atkSpeed)
		{
			float num = Mathf.Clamp(atkSpeed * _attackSpeedScale, _minMotionMul, _maxMotionMul);
			if (!Mathf.Approximately(_lastAttackSpeedMul, num))
			{
				_lastAttackSpeedMul = num;
				_animator.SetFloat(HashAttackSpeedMul, num);
			}
		}

		public void SetMoveSpeed(float moveSpeed)
		{
			float num = Mathf.Clamp(moveSpeed * _moveSpeedScale, _minMotionMul, _maxMotionMul);
			if (!Mathf.Approximately(_lastMoveSpeedMul, num))
			{
				_lastMoveSpeedMul = num;
				_animator.SetFloat(HashMoveSpeedMul, num);
			}
		}

		public void PlayHit()
		{
			_animator.SetTrigger(HashHit);
		}

		public void PlayDead()
		{
			_animator.SetBool(HashIsDead, true);
			_animator.SetTrigger(HashHDead);
		}

		public void ResetDead()
		{
			_animator.ResetTrigger(HashHDead);
			_animator.SetBool(HashIsDead, false);
		}

		public void SetController(RuntimeAnimatorController controller)
		{
			if (!(controller == null) && !(_animator.runtimeAnimatorController == controller))
			{
				_animator.runtimeAnimatorController = controller;
				_animator.ResetTrigger(HashAttack);
				_animator.ResetTrigger(HashSkill);
				_animator.ResetTrigger(HashHit);
				_animator.ResetTrigger(HashHDead);
				_lastIsMoving = _animator.GetBool(HashIsMoving);
			}
		}
	}
}
