using UnityEngine;

namespace ProjectOne.Unit
{
  public class UnitAnimator : MonoBehaviour
  {
    private Animator _animator;
    private SpriteRenderer _spriteRenderer;
    private bool _lastIsMoving;

    // 스탯 → 모션 배율 변환 스케일 (mul = stat * scale)
    [SerializeField] private float _attackSpeedScale = 100.0f;
    [SerializeField] private float _moveSpeedScale   = 100.0f;
    [SerializeField] private float _minMotionMul = 0.1f;
    [SerializeField] private float _maxMotionMul = 5.0f;

    private float _lastAttackSpeedMul = 1f;
    private float _lastMoveSpeedMul   = 1f;

    // 파라미터 해시 캐시
    private static readonly int HashIsMoving        = Animator.StringToHash("IsMoving");
    private static readonly int HashAttack          = Animator.StringToHash("Attack");
    private static readonly int HashSkill           = Animator.StringToHash("Skill");
	private static readonly int HashHit             = Animator.StringToHash("Hit");
    private static readonly int HashIsDead          = Animator.StringToHash("IsDead");
    private static readonly int HashAttackSpeedMul  = Animator.StringToHash("AttackSpeedMul");
    private static readonly int HashMoveSpeedMul    = Animator.StringToHash("MoveSpeedMul");

    private void Awake()
    {
      _animator       = GetComponentInChildren<Animator>();
      _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    public void SetMoving(bool isMoving)
    {
      // 동일 값 중복 호출 가드
      if (_lastIsMoving == isMoving)
      {
        return;
      }
      _lastIsMoving = isMoving;
      _animator.SetBool(HashIsMoving, isMoving);
    }

    // Facing.x 기준 좌우 반전
    public void SetFacing(Vector2 facing)
    {
      bool flip = facing.x < 0f;
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

    // 테이블 MotionName 기반 트리거 — 이름이 비어있으면 모션 없음
    public void PlayMotion(string motionName)
    {
      if (string.IsNullOrEmpty(motionName) == true)
      {
        return;
      }
      _animator.SetTrigger(Animator.StringToHash(motionName));
    }

    // 공격/스킬 상태 모션 속도 — 공격속도 스탯 원시값을 받아 내부에서 mul로 변환
    public void SetAttackSpeed(float atkSpeed)
    {
      float mul = Mathf.Clamp(atkSpeed * _attackSpeedScale, _minMotionMul, _maxMotionMul);
      if (Mathf.Approximately(_lastAttackSpeedMul, mul))
      {
        return;
      }
      _lastAttackSpeedMul = mul;
      _animator.SetFloat(HashAttackSpeedMul, mul);
    }

    // 이동 상태 모션 속도 — 이동속도 스탯 원시값을 받아 내부에서 mul로 변환
    public void SetMoveSpeed(float moveSpeed)
    {
      float mul = Mathf.Clamp(moveSpeed * _moveSpeedScale, _minMotionMul, _maxMotionMul);
      if (Mathf.Approximately(_lastMoveSpeedMul, mul))
      {
        return;
      }
      _lastMoveSpeedMul = mul;
      _animator.SetFloat(HashMoveSpeedMul, mul);
    }

    public void PlayHit()
    {
      _animator.SetTrigger(HashHit);
    }

    public void SetDead(bool isDead)
    {
      _animator.SetBool(HashIsDead, isDead);
    }

    // 유닛별 모션 세트 교체 (베이스 컨트롤러 공유 가정)
    public void SetController(RuntimeAnimatorController controller)
    {
      if (controller == null)
      {
        return;
      }
      if (_animator.runtimeAnimatorController == controller)
      {
        return;
      }

      _animator.runtimeAnimatorController = controller;

      // 잔여 트리거 큐 제거
      _animator.ResetTrigger(HashAttack);
      _animator.ResetTrigger(HashSkill);
      _animator.ResetTrigger(HashHit);

      // 캐시 동기화 (새 컨트롤러도 동일 파라미터 스키마 가정)
      _lastIsMoving = _animator.GetBool(HashIsMoving);
    }
  }
}
