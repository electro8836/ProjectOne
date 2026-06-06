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
	private bool _facingLocked;   // 캐스팅 중 조준 고정 — SetFacing/자동 갱신 무시
	private bool _overridePierce;   // override 이동 중 유닛 충돌 무시(벽만 차단) — 대시 공격 관통용
	private bool _overrideBlocked;  // override 이동이 다음 위치로 갈 수 없어 막힌 상태(latch) — 코드 버프가 종료 판정에 사용
	private readonly float _moveSpeedMultiplier = 0.1f;
	private readonly float _knockbackMultiplier = 0.1f;

	// 공간 해시 조회 결과 재사용 버퍼 — 충돌 검사마다 채워 씀 (할당 방지)
	private readonly List<UnitBase> _queryBuffer = new List<UnitBase>(32);

	public Vector2 Facing { get; private set; } = Vector2.right;
	public bool IsMoving    { get { return _moveVelocity.sqrMagnitude > 0.01f; } }
	public bool IsImpulsed  { get { return _impulseVelocity.sqrMagnitude > 0.01f; } }
	public bool MoveEnabled { get { return _moveEnabled; } }
	public bool OverrideBlocked { get { return _overrideBlocked; } }

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

		// Facing 갱신 — override(대시 등) 중엔 이동(override) 방향을 바라봄(입력 기반 Facing 무시), 평소엔 Move 채널 기준
		// 캐스팅 중(_facingLocked)에는 조준을 시작 방향으로 고정
		if (_facingLocked == false)
		{
			if (_hasOverride == true)
			{
				if (_overrideVelocity.sqrMagnitude > 0.01f)
				{
					Facing = _overrideVelocity.normalized;
				}
			}
			else if (_moveVelocity.sqrMagnitude > 0.01f)
			{
				Facing = _moveVelocity.normalized;
			}
		}
	}

	private void ApplyMovement(Vector2 velocity)
	{
		Vector2 currentPos = transform.position;
		Vector2 nextPos    = currentPos + velocity * Time.fixedDeltaTime;

		// override 이동(대시/대시 공격): 슬라이딩 없이 다음 위치 판정만 — 갈 수 없으면 그 자리에서 정지하고 막힘 latch
		if (_hasOverride == true)
		{
			// 관통은 벽(IsWalkable)만, 비관통은 벽+유닛(CanMoveTo) 판정
			bool canMove = _overridePierce == true ? IsWalkable(nextPos) : CanMoveTo(currentPos, nextPos);
			if (canMove == true)
			{
				transform.position = nextPos;
			}
			else
			{
				_overrideBlocked = true;
			}

			return;
		}

		if (CanMoveTo(currentPos, nextPos) == true)
		{
			transform.position = nextPos;
			return;
		}

		// 유닛-유닛 원형 충돌: 법선 방향 성분 제거 슬라이딩 (리지드바디와 동일한 원리)
		Vector2 slideVel = ComputeCircleSlide(currentPos, velocity);
		if (slideVel.sqrMagnitude > 0.001f)
		{
			Vector2 slidePos = currentPos + slideVel * Time.fixedDeltaTime;
			if (CanMoveTo(currentPos, slidePos) == true)
			{
				transform.position = slidePos;
				return;
			}
		}

		// 타일맵 등 직교 장애물: 축 분리 슬라이딩 (기존 방식 유지)
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

		// 모두 막혔으면 이동 안 함
	}

	// 원래 velocity로 이동 시 새로 겹치는 유닛들의 충돌 법선을 기준으로 슬라이딩 속도를 계산한다.
	// 각 충돌 유닛에 대해 법선 방향 성분을 제거 → 남은 성분이 미끄러지는 방향.
	private Vector2 ComputeCircleSlide(Vector2 currentPos, Vector2 velocity)
	{
		if (UnitContainer.Instance == null)
		{
			return velocity;
		}

		Vector2 slideVel = velocity;
		Vector2 nextPos  = currentPos + velocity * Time.fixedDeltaTime;

		// 인접 셀 후보만 조회 — 전체 유닛 brute-force 순회 회피
		UnitContainer.Instance.SpatialHash.Query(nextPos, _queryBuffer);
		for (int i = 0; i < _queryBuffer.Count; i++)
		{
			UnitBase u = _queryBuffer[i];
			if (u == null || u.transform == transform || u.IsDead == true)
			{
				continue;
			}

			float min    = (_unitRadius + u.CachedRadius) * 0.5f;
			float minSqr = min * min;
			Vector2 up   = u.CachedPos;

			// 이미 겹쳐 있는 유닛은 무시 (끼임에서 빠져나올 수 있게)
			if ((up - currentPos).sqrMagnitude < minSqr)
			{
				continue;
			}

			// 원래 velocity로 이동 시 새로 겹치는 유닛만 처리
			if ((up - nextPos).sqrMagnitude >= minSqr)
			{
				continue;
			}

			// 충돌 법선: 상대 유닛 → 자신 (밀려나야 할 방향)
			Vector2 normal = currentPos - up;
			if (normal.sqrMagnitude < 1e-6f)
			{
				continue;
			}
			normal = normal.normalized;

			// 법선 방향으로 이동 중인 성분만 제거 (충돌체 쪽으로 파고드는 성분)
			float dot = Vector2.Dot(slideVel, normal);
			if (dot < 0f)
			{
				slideVel -= dot * normal;
			}
		}

		return slideVel;
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

		// 인접 셀 후보만 조회 — 전체 유닛 brute-force 순회 회피
		UnitContainer.Instance.SpatialHash.Query(nextPos, _queryBuffer);
		for (int i = 0; i < _queryBuffer.Count; i++)
		{
			UnitBase u = _queryBuffer[i];
			if (u == null || u.transform == transform || u.IsDead == true)
			{
				continue;
			}

			float min = (_unitRadius + u.CachedRadius) * 0.5f;
			float minSqr = min * min;
			Vector2 up = u.CachedPos;

			// 이미 겹쳐 있던 유닛은 무시 — 그쪽에서 빠져나오는 이동은 허용
			if ((up - currentPos).sqrMagnitude < minSqr)
			{
				continue;
			}

			// 이동 후 새로 겹치게 되면 차단
			if ((up - nextPos).sqrMagnitude < minSqr)
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
		if (_facingLocked == true)
		{
			return;
		}

		if (direction.sqrMagnitude < 0.01f)
		{
			return;
		}

		Facing = direction.normalized;
	}

	// 캐스팅 중 조준 고정 토글 — 잠금 시 SetFacing/자동 facing 갱신을 무시한다
	public void SetFacingLocked(bool locked)
	{
		_facingLocked = locked;
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

	public void SetOverride(Vector2 velocity, bool pierceUnits = false)
	{
		_overrideVelocity = velocity;
		_hasOverride      = true;
		_overridePierce   = pierceUnits;
		_overrideBlocked  = false;
	}

	public void ClearOverride()
	{
		_overrideVelocity = Vector2.zero;
		_hasOverride      = false;
		_overridePierce   = false;
	}
}
