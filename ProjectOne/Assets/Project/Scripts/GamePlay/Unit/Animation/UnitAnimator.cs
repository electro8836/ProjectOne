using UnityEngine;

namespace ProjectOne.Unit
{
	public class UnitAnimator : MonoBehaviour
	{
		private Animator _animator;

		private SpriteRenderer _spriteRenderer;

		private bool _lastIsMoving;

		[SerializeField]
		private float _attackSpeedScale = 100f;

		[SerializeField]
		private float _moveSpeedScale = 100f;

		[SerializeField]
		private float _minMotionMul = 0.1f;

		[SerializeField]
		private float _maxMotionMul = 5f;

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
			bool flag = facing.x < 0f;
			if (_spriteRenderer.flipX != flag)
			{
				_spriteRenderer.flipX = flag;
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
