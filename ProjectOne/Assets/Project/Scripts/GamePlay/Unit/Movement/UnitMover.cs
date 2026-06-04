using UnityEngine;
using ProjectOne.Map;
public class UnitMover : MonoBehaviour
{
	private float _unitRadius = 0.3f;
	private float _inverseMass = 1f;
	private float _effectiveDrag = 10f;
	private Vector2 _moveVelocity;
	private bool _moveEnabled = true;
	private Vector2 _impulseVelocity;
	[SerializeField] private float _impulseDrag = 10f;
	private Vector2 _overrideVelocity;
	private bool _hasOverride;
	private readonly float _moveSpeedMultiplier = 0.1f;
	private readonly float _knockbackMultiplier = 0.1f;

	public Vector2 Facing { get; private set; } = Vector2.right;
	public bool IsMoving    { get { return _moveVelocity.sqrMagnitude > 0.01f; } }
	public bool IsImpulsed  { get { return _impulseVelocity.sqrMagnitude > 0.01f; } }
	public bool MoveEnabled { get { return _moveEnabled; } }

	private void FixedUpdate()
	{
		// 우선순위: Override > Move + Impulse
		Vector2 finalVelocity;

		if (_hasOverride == true)
		{
			finalVelocity = _overrideVelocity;
		}
		else
		{
			Vector2 move = Vector2.zero;
			if (_moveEnabled == true)
			{
				move = _moveVelocity;
			}

			// Impulse 감쇄 (mass가 클수록 초기 속도 낮음 + 감쇄도 빠름)
			if (_impulseVelocity.sqrMagnitude > 0f)
			{
				_impulseVelocity = Vector2.MoveTowards(_impulseVelocity, Vector2.zero, _effectiveDrag * Time.fixedDeltaTime);
			}

			finalVelocity = move + _impulseVelocity;
		}

		if (finalVelocity.sqrMagnitude > 0.001f)
		{
			ApplyMovement(finalVelocity);
		}

		// Facing은 Move 채널 기준으로만 갱신
		if (_moveVelocity.sqrMagnitude > 0.01f)
		{
			Facing = _moveVelocity.normalized;
		}
	}

	private void ApplyMovement(Vector2 velocity)
	{
		Vector2 currentPos = transform.position;
		Vector2 nextPos    = currentPos + velocity * Time.fixedDeltaTime;

		if (IsWalkable(nextPos) == true)
		{
			transform.position = nextPos;
			return;
		}

		// 막혔으면 축 분리 슬라이딩 시도
		Vector2 moveOnlyX = new Vector2(nextPos.x, currentPos.y);
		Vector2 moveOnlyY = new Vector2(currentPos.x, nextPos.y);

		if (IsWalkable(moveOnlyX) == true)
		{
			transform.position = moveOnlyX;
		}
		else if (IsWalkable(moveOnlyY) == true)
		{
			transform.position = moveOnlyY;
		}

		// 둘 다 막혔으면 이동 안 함
	}

	private bool IsWalkable(Vector2 position)
	{
		if (TilemapGrid.Instance == null)
		{
			return true;
		}

		return TilemapGrid.Instance.IsWalkable(position, _unitRadius);
	}

	public void Initialize(float radius, float mass)
	{
		_unitRadius = radius;

		float safeMass = Mathf.Max(0.01f, mass);
		_inverseMass = 1f / safeMass;
		_effectiveDrag = _impulseDrag * safeMass;
	}

	public void Move(Vector2 direction, float speed)
	{
		_moveVelocity = direction.normalized * speed * _moveSpeedMultiplier;
	}

	public void Stop()
	{
		_moveVelocity = Vector2.zero;
	}

	public void SetFacing(Vector2 direction)
	{
		if (direction.sqrMagnitude < 0.01f)
		{
			return;
		}

		Facing = direction.normalized;
	}

	public void SetMoveEnabled(bool enabled)
	{
		_moveEnabled = enabled;
		if (_moveEnabled == false)
		{
			_moveVelocity = Vector2.zero;
		}
	}

	public void AddImpulse(Vector2 force)
	{
		Vector2 newVelocity = force * _inverseMass * _knockbackMultiplier;
		if (newVelocity.sqrMagnitude > _impulseVelocity.sqrMagnitude)
		{
			_impulseVelocity = newVelocity;
		}
	}

	public void ClearImpulse()
	{
		_impulseVelocity = Vector2.zero;
	}

	public void SetOverride(Vector2 velocity)
	{
		_overrideVelocity = velocity;
		_hasOverride      = true;
	}

	public void ClearOverride()
	{
		_overrideVelocity = Vector2.zero;
		_hasOverride      = false;
	}
}
