using UnityEngine;
using System.Collections.Generic;
using ProjectOne.Map;
using ProjectOne.Unit;
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

		if (CanMoveTo(currentPos, nextPos) == true)
		{
			transform.position = nextPos;
			return;
		}

		// 막혔으면 축 분리 슬라이딩 시도
		Vector2 moveOnlyX = new Vector2(nextPos.x, currentPos.y);
		Vector2 moveOnlyY = new Vector2(currentPos.x, nextPos.y);

		if (CanMoveTo(currentPos, moveOnlyX) == true)
		{
			transform.position = moveOnlyX;
		}
		else if (CanMoveTo(currentPos, moveOnlyY) == true)
		{
			transform.position = moveOnlyY;
		}

		// 둘 다 막혔으면 이동 안 함
	}

	// 타일맵 통과 + 다른 유닛과 새로 겹치지 않을 때만 이동 허용
	private bool CanMoveTo(Vector2 currentPos, Vector2 nextPos)
	{
		if (IsWalkable(nextPos) == false)
		{
			return false;
		}

		if (OverlapsNewUnit(currentPos, nextPos) == true)
		{
			return false;
		}

		return true;
	}

	private bool IsWalkable(Vector2 position)
	{
		if (TilemapGrid.Instance == null)
		{
			return true;
		}

		return TilemapGrid.Instance.IsWalkable(position, _unitRadius);
	}

	// nextPos 에서 다른 유닛과 "새로" 겹치는지 — 이미 겹친 유닛은 무시(끼임에서 빠져나올 수 있게)
	private bool OverlapsNewUnit(Vector2 currentPos, Vector2 nextPos)
	{
		if (UnitContainer.Instance == null)
		{
			return false;
		}

		IReadOnlyList<UnitBase> all = UnitContainer.Instance.All;
		for (int i = 0; i < all.Count; i++)
		{
			UnitBase u = all[i];
			if (u == null || u.transform == transform || u.IsDead == true)
			{
				continue;
			}

			float min = (_unitRadius + u.Radius) * 0.5f;
			float minSqr = min * min;
			Vector2 up = u.transform.position;

			// 이미 겹쳐 있던 유닛은 무시 — 그쪽에서 빠져나오는 이동은 허용
			if (((Vector2)up - currentPos).sqrMagnitude < minSqr)
			{
				continue;
			}

			// 이동 후 새로 겹치게 되면 차단
			if (((Vector2)up - nextPos).sqrMagnitude < minSqr)
			{
				return true;
			}
		}

		return false;
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
