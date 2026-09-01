using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectOne.Unit
{
	public class UnitAnimator : MonoBehaviour
	{
		// 정렬 단계가 바뀔 때 통지한다.
		// SortingGroup 밖에서 유닛을 따라가야 하는 렌더러(HP바 등)가 구독한다.
		public event System.Action<int> SortingOrderChanged;

		private Animator _animator;

		private SpriteRenderer _spriteRenderer;

		// UnitRoot 에 붙은 정렬 그룹. 유닛 간 앞뒤는 이 값으로만 제어한다.
		private SortingGroup _sortingGroup;

		private MaterialPropertyBlock _mpb;

		private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

		private bool _lastIsMoving;

		private bool _lastDebuff;

		// 좌우 플립 판정용 — facing.x 를 시간 기반으로 누적해 고빈도 부호 진동(분산 조향 떨림)을 흡수
		private float _facingXSmoothed;

		private const float FacingSmoothRate = 10f;

		private const float FacingDeadband = 0.05f;

		// 좌우반전 대상. 인스펙터에서 UnitRoot 를 지정한다.
		//
		// 파츠 캐릭터(SpriteRenderer 30개)는 SpriteRenderer.flipX 로 뒤집을 수 없다 —
		// 애니메이션이 각 파츠의 위치·회전으로 만들어지므로 계층 전체를 뒤집어야 한다.
		[SerializeField]
		private Transform _flipRoot;

		[SerializeField]
		private float _outlineWidth = 0.0002f;

		// Stat_AtkSpeed 가 곧 애니메이션 배속이다 (스킬 설계 4.2 의 useSpeed 와 같은 개념).
		[SerializeField]
		private float _attackSpeedScale = 1f;

		// 걷기 애니 배속의 프리팹별 미세 조정 계수.
		// 호출자가 "기본 이속 대비 현재 이속" 비율을 넘기므로 기본값 1이면 기본 이속에서 1배속이 된다.
		[SerializeField]
		private float _moveSpeedScale = 1f;

		[SerializeField]
		private float _minMotionMul = 0.1f;

		[SerializeField]
		private float _maxMotionMul = 5f;

		// worldY → sortingOrder 변환 정밀도. sortingOrder는 int16(±32767)이므로
		// (맵 Y폭 × _precision ≤ 32767) 와 (구분할 최소 Y간격 × _precision ≥ 1) 사이에서 잡는다.
		[SerializeField]
		private float _precision = 1000f;

		// 루트가 발밑이 아닐 때 정렬 기준점(발밑)을 맞추는 보정값
		[SerializeField]
		private float _yOffset = 0f;

		// 컨트롤러에 존재하는 파라미터 해시. Awake 에서 수집하고 SetController 에서 다시 만든다.
		private readonly HashSet<int> _parameterHashes = new HashSet<int>();

		// 프리팹 원본 컨트롤러 — 무기 오버라이드를 벗을 때 복귀 대상
		private RuntimeAnimatorController _baseController;

		private int _lastSortOrder = int.MinValue;

		// 마지막으로 계산된 정렬 단계. 아직 한 번도 계산되지 않았으면 int.MinValue.
		public int SortingOrder { get { return _lastSortOrder; } }

		private float _lastAttackSpeedMul = 1f;

		private float _lastMoveSpeedMul = 1f;

		private static readonly int HashIsMoving = Animator.StringToHash("IsMoving");

		private static readonly int HashAttack = Animator.StringToHash("Attack");

		private static readonly int HashSkill = Animator.StringToHash("Skill");

		private static readonly int HashHit = Animator.StringToHash("Hit");

		private static readonly int HashHDead = Animator.StringToHash("Die");

		private static readonly int HashIsDead = Animator.StringToHash("IsDead");

		// 몬스터 컨트롤러엔 아직 캐스팅 모션이 없다 — hasParameter 가드로 조용히 넘어간다.
		private static readonly int HashIsCasting = Animator.StringToHash("IsCasting");

		// 기절 등 지속 디버프 포즈 — AnyState 에서 DEBUFF 상태로 넘어가는 조건이다.
		private static readonly int HashDebuff = Animator.StringToHash("Debuff");

		private static readonly int HashAttackSpeedMul = Animator.StringToHash("AttackSpeedMod");

		private static readonly int HashMoveSpeedMul = Animator.StringToHash("MoveSpeedMod");

		private void Awake()
		{
			_animator = this.GetComponentInChildren<Animator>();
			_spriteRenderer = this.GetComponentInChildren<SpriteRenderer>();
			_sortingGroup = this.GetComponentInChildren<SortingGroup>();
			_mpb = new MaterialPropertyBlock();

			// 미지정 프리팹은 애니메이터가 붙은 계층을 뒤집는다 — 대개 그것이 시각 루트다.
			if (_flipRoot == null && _animator != null)
			{
				_flipRoot = _animator.transform;
			}

			// 프리팹이 물고 있던 원본 컨트롤러 — 무기를 벗으면 여기로 되돌린다.
			if (_animator != null)
			{
				_baseController = _animator.runtimeAnimatorController;
			}

			cacheParameters();
		}

		// 컨트롤러에 실제로 존재하는 파라미터 해시를 모아 둔다.
		//
		// 없는 파라미터에 Set 을 부르면 유니티가 매 호출마다 경고를 낸다.
		// 컨트롤러는 아트 자산이라 코드보다 늦게 완성되는 것이 정상이므로(캐스팅 모션 등),
		// 없으면 조용히 건너뛰고 나중에 추가되면 자동으로 먹게 한다.
		private void cacheParameters()
		{
			_parameterHashes.Clear();
			if (_animator == null || _animator.runtimeAnimatorController == null)
			{
				return;
			}

			AnimatorControllerParameter[] parameters = _animator.parameters;
			for (int i = 0; i < parameters.Length; i++)
			{
				_parameterHashes.Add(parameters[i].nameHash);
			}
		}

		private bool hasParameter(int hash)
		{
			return _parameterHashes.Contains(hash);
		}

		// 발밑(피벗 하단) Y좌표를 정수 sortingOrder로 변환해 유닛 간 앞뒤 정렬을 결정한다.
		// UnitBase.ManualTick 이 프레임당 1회 호출 — 개별 MonoBehaviour.LateUpdate 콜백 오버헤드 제거.
		public void UpdateSorting()
		{
			float sortY = transform.position.y + _yOffset;
			int order = -Mathf.RoundToInt(sortY * _precision); // Y가 클수록(위) 뒤로 → 음수
			if (_lastSortOrder == order)
			{
				return;
			}

			_lastSortOrder = order;

			// UnitRoot 의 SortingGroup 이 유닛 간 앞뒤를 결정한다.
			// 자식 파츠의 sortingOrder 는 그룹 내부 상대 순서이므로 건드리지 않는다.
			if (_sortingGroup)
			{
				_sortingGroup.sortingOrder = order;
			}
			// SortingGroup 이 없는 프리팹(몬스터: Model SpriteRenderer 1개)은 렌더러에 직접 쓴다.
			else if (_spriteRenderer)
			{
				_spriteRenderer.sortingOrder = order;
			}

			// 그룹 밖에서 유닛을 따라다니는 렌더러(HP바 등)에 통지
			if (SortingOrderChanged != null)
			{
				SortingOrderChanged(order);
			}
		}

		public void SetMoving(bool isMoving)
		{
			if (_lastIsMoving != isMoving && hasParameter(HashIsMoving) == true)
			{
				_lastIsMoving = isMoving;
				_animator.SetBool(HashIsMoving, isMoving);
			}
		}

		// 지속 디버프 포즈 토글. SetMoving 과 같이 값이 바뀔 때만 기록한다.
		public void SetDebuff(bool active)
		{
			if (_lastDebuff != active && hasParameter(HashDebuff) == true)
			{
				_lastDebuff = active;
				_animator.SetBool(HashDebuff, active);
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

		// 스케일 X 부호로 뒤집는다. **+1 이 왼쪽**이다(원본 스프라이트가 좌향).
		//
		// 절대값을 유지하는 이유 — 프리팹마다 시각 루트의 배율이 다를 수 있고(몬스터 Model 은 0.8),
		// 부호만 바꿔야 크기가 보존된다.
		private void ApplyFlip(bool faceLeft)
		{
			if (_flipRoot == null)
			{
				return;
			}

			Vector3 scale = _flipRoot.localScale;
			float magnitude = Mathf.Abs(scale.x);
			float signed = faceLeft ? magnitude : -magnitude;

			if (scale.x == signed)
			{
				return;
			}

			scale.x = signed;
			_flipRoot.localScale = scale;
		}

		public void PlayAttack()
		{
			if (hasParameter(HashAttack) == true)
			{
				_animator.SetTrigger(HashAttack);
			}
		}

		public void PlaySkill()
		{
			if (hasParameter(HashSkill) == true)
			{
				_animator.SetTrigger(HashSkill);
			}
		}

		public void PlayMotion(string motionName)
		{
			if (string.IsNullOrEmpty(motionName) == true)
			{
				return;
			}

			int hash = Animator.StringToHash(motionName);
			if (hasParameter(hash) == true)
			{
				_animator.SetTrigger(hash);
			}
		}

		// 캐스팅(시전) 지속 상태 설정 — 시작 시 true, 발동/취소/사망 시 false
		public void SetCasting(bool isCasting)
		{
			if (hasParameter(HashIsCasting) == true)
			{
				_animator.SetBool(HashIsCasting, isCasting);
			}
		}

		public void SetAttackSpeed(float atkSpeed)
		{
			float num = Mathf.Clamp(atkSpeed * _attackSpeedScale, _minMotionMul, _maxMotionMul);
			if (!Mathf.Approximately(_lastAttackSpeedMul, num) && hasParameter(HashAttackSpeedMul) == true)
			{
				_lastAttackSpeedMul = num;
				_animator.SetFloat(HashAttackSpeedMul, num);
			}
		}

		// ratio 는 기본 이속 대비 현재 이속(기본 이속이면 1). 절대 속도가 아니다.
		public void SetMoveSpeedRatio(float ratio)
		{
			float num = Mathf.Clamp(ratio * _moveSpeedScale, _minMotionMul, _maxMotionMul);
			if (!Mathf.Approximately(_lastMoveSpeedMul, num) && hasParameter(HashMoveSpeedMul) == true)
			{
				_lastMoveSpeedMul = num;
				_animator.SetFloat(HashMoveSpeedMul, num);
			}
		}

		public void PlayHit()
		{
			if (hasParameter(HashHit) == true)
			{
				_animator.SetTrigger(HashHit);
			}
		}

		public void PlayDead()
		{
			if (hasParameter(HashIsDead) == true)
			{
				_animator.SetBool(HashIsDead, true);
			}

			if (hasParameter(HashHDead) == true)
			{
				_animator.SetTrigger(HashHDead);
			}
		}

		public void ResetDead()
		{
			if (hasParameter(HashHDead) == true)
			{
				_animator.ResetTrigger(HashHDead);
			}

			if (hasParameter(HashIsDead) == true)
			{
				_animator.SetBool(HashIsDead, false);
			}
		}

		// 풀 재사용 — 비활성/리바인드로 애니메이터 파라미터가 기본값으로 돌아가면 캐시와 어긋난다.
		// 어긋나면 SetMoving 이 값 비교에서 걸러져 이동해도 Idle 포즈가 유지된다.
		// 컨트롤러 교체(SetController)와 같은 정리를 스폰 시점에도 한다.
		public void ResetForSpawn()
		{
			if (_animator == null)
			{
				return;
			}

			if (hasParameter(HashAttack) == true)
			{
				_animator.ResetTrigger(HashAttack);
			}

			if (hasParameter(HashSkill) == true)
			{
				_animator.ResetTrigger(HashSkill);
			}

			if (hasParameter(HashHit) == true)
			{
				_animator.ResetTrigger(HashHit);
			}

			if (hasParameter(HashHDead) == true)
			{
				_animator.ResetTrigger(HashHDead);
			}

			if (hasParameter(HashIsCasting) == true)
			{
				_animator.SetBool(HashIsCasting, false);
			}

			if (hasParameter(HashIsMoving) == true)
			{
				_animator.SetBool(HashIsMoving, false);
			}

			if (hasParameter(HashDebuff) == true)
			{
				_animator.SetBool(HashDebuff, false);
			}

			_lastIsMoving = false;
			_lastDebuff = false;

			// 스로틀 캐시도 버린다 — 같은 값이 다시 들어올 때 걸러지지 않게 한다.
			_lastAttackSpeedMul = float.NaN;
			_lastMoveSpeedMul = float.NaN;
		}

		private void OnValidate()
		{
			if (_spriteRenderer == null)
			{
				_spriteRenderer = this.GetComponentInChildren<SpriteRenderer>();
			}
			if (_spriteRenderer == null) { return; }
			if (_mpb == null)
			{
				_mpb = new MaterialPropertyBlock();
			}
			_spriteRenderer.GetPropertyBlock(_mpb);
			_mpb.SetFloat(OutlineWidthId, _outlineWidth);
			_spriteRenderer.SetPropertyBlock(_mpb);
		}

		public void SetOutlineEnabled(bool enabled)
		{
			if (_spriteRenderer == null) { return; }
			_spriteRenderer.GetPropertyBlock(_mpb);
			_mpb.SetFloat(OutlineWidthId, enabled ? _outlineWidth : 0f);
			_spriteRenderer.SetPropertyBlock(_mpb);
		}

		public void SetController(RuntimeAnimatorController controller)
		{
			if (!(controller == null) && !(_animator.runtimeAnimatorController == controller))
			{
				_animator.runtimeAnimatorController = controller;

				// 컨트롤러가 바뀌면 파라미터 구성도 바뀔 수 있다 — 해시 캐시를 다시 만든다.
				cacheParameters();

				_animator.ResetTrigger(HashAttack);
				_animator.ResetTrigger(HashSkill);
				_animator.ResetTrigger(HashHit);
				_animator.ResetTrigger(HashHDead);
				if (hasParameter(HashIsCasting) == true)
				{
					_animator.SetBool(HashIsCasting, false);
				}

				_lastIsMoving = _animator.GetBool(HashIsMoving);

				// 컨트롤러 교체는 애니메이터를 리바인드해 Float 파라미터를 기본값으로 되돌린다.
				// 스로틀 캐시도 함께 버려야 같은 값이 다시 들어올 때 걸러지지 않는다.
				_lastAttackSpeedMul = float.NaN;
				_lastMoveSpeedMul = float.NaN;
			}
		}

		// 무기를 벗었을 때 프리팹 원본 컨트롤러로 되돌린다.
		public void RestoreBaseController()
		{
			SetController(_baseController);
		}
	}
}
