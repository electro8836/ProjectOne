using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectOne.Unit
{
	// 히어로 무기 조준·배치. 손에 달지 않고 손이 있을 자리(WeaponAim)에 두고 facing 방향으로 돌린다.
	//
	// 계층 역할: WeaponAim(중심·조준 회전, 이 컴포넌트 / 위치 = 흔들림, WeaponSway 레이어)
	//   → Offset(조준각별 위치, WeaponOffset 레이어) → Motion(자세·공격 모션, Weapon 레이어) → Model(무기 스프라이트, HeroAvatar)
	// 조준각별 오프셋·공격 모션은 블렌드 트리가 AimAngle 파라미터로 고른다. 이 컴포넌트는 그 값을 넘기기만 한다.
	// 공격 중에도 WeaponAim 은 조준각을 유지한다 — 같은 공격 모션이 바라보는 방향으로 나간다.
	// WeaponAim 회전은 클립이 키를 잡으면 안 된다(이 컴포넌트가 쓴다). 위치는 흔들림 레이어만 쓰고 공격 중엔 0 이라
	// 공격 판정·궤적(SlashFX) 기준점이 콜라이더 중심과 어긋나지 않는다.
	//
	// Hero.ManualTick 이 base(UnitAnimator 좌우 반전) 직후 Tick 을 부른다. 반전 결과를 읽어야 각이 맞는다.
	public class HeroWeapon : MonoBehaviour
	{
		private static readonly int HashAimAngle = Animator.StringToHash("AimAngle");

		// AC_Hero Weapon 레이어 공격 상태(WEAPON_ATTACK·_2·_3)의 태그
		private static readonly int HashAttackTag = Animator.StringToHash("Attack");

		// 공격 중이면 WeaponOffset 레이어가 이동 중에도 정지 오프셋을 쓴다 — 공격 클립이 정지 오프셋 기준이라
		private static readonly int HashWeaponAttacking = Animator.StringToHash("WeaponAttacking");

		private const string WeaponLayerName = "Weapon";

		private const string SwayLayerName = "WeaponSway";

		// 조준각이 이보다 아래(+)면 몸 뒤에 그린다 — 정렬 힌트가 0(각도 규칙)일 때만
		private const float BackAngle = 45f;

		// 좌우반전 루트(UnitRoot). 같은 오브젝트의 Animator 를 쓴다.
		[SerializeField] private Transform _flipRoot;

		// 위치(캐릭터 중심축)는 프리팹 값 그대로 두고 회전만 쓴다.
		[SerializeField] private Transform _weaponAim;

		[SerializeField] private Transform _weaponModel;

		// 무기 정렬 그룹(WeaponAim). 몸(Body) 그룹과 나눠, 몸 순서를 기준으로 앞뒤만 따로 정한다 —
		// 몸 앞 구간이면 칼이 닿는 거리 안의 아래쪽(더 가까운) 몬스터보다도 위에 그린다.
	// 궤적(SlashFX)은 자체 그룹(sortAtRoot, Effect 레이어)으로 이 그룹을 벗어나 몬스터·지형지물보다 항상 위에 그린다.
		[SerializeField] private SortingGroup _weaponGroup;

		// 몸 앞 구간에서 몸 순서에 더하는 값 — 공격 사거리(2) × UnitAnimator 정렬 정밀도(1000)
		[SerializeField] private int _frontSortOffset = 2000;

		private Animator _animator;

		private UnitAnimator _unitAnimator;

		// 흔들림이 공격 뒤 다시 살아나는 시간(초) — 공격 종료 전이(0.1초)와 맞춘다. 공격 시작 땐 즉시 끈다.
		[SerializeField] private float _swayRecoverTime = 0.1f;

		// 앞뒤 판정 — 클립이 Model 의 sortingOrder 를 정렬 힌트로 키를 잡는다.
		// 양수 = 무조건 몸 앞, 음수 = 무조건 몸 뒤, 0 = 조준각 규칙(BackAngle 아래면 뒤)
		private SpriteRenderer _modelRenderer;

		private int _weaponLayer = -1;

		private int _swayLayer = -1;

		// 마지막으로 쓴 흔들림 레이어 가중치. 바뀔 때만 쓴다(NaN = 다시 써야 함).
		private float _swayWeight = float.NaN;

		private bool _hasAttackingParam;

		// 마지막으로 보낸 공격 여부. 바뀔 때만 쓴다.
		private bool _isAttacking;

		// 컨트롤러 교체 직후 — 값이 같아도 한 번은 다시 보낸다
		private bool _attackStateDirty = true;

		private int _lastWeaponOrder = int.MinValue;

		// 컨트롤러가 바뀌면(무기 AOC 교체) 리바인드로 파라미터가 기본값이 된다 — 바뀐 것을 알아채 다시 보낸다.
		private RuntimeAnimatorController _lastController;

		private bool _hasAimParam;

		// 마지막으로 적용한 조준각. 차이가 이보다 작으면 회전·파라미터 쓰기를 건너뛴다.
		// 트랜스폼은 같은 값을 써도 자식 계층에 변경이 전파되므로 바뀔 때만 쓴다.
		private float _lastAngle = float.NaN;

		// 갱신 기준 각 — 각이 바뀐 프레임마다 Animator 를 한 번 더 평가하므로(Tick 참고),
		// 아날로그 입력으로 방향을 틀 때 매 프레임 재평가되지 않게 눈에 띄지 않는 0.5도 단위로만 갱신한다.
		private const float AngleEpsilon = 0.5f;

		private void Awake()
		{
			if (_flipRoot != null)
			{
				_animator = _flipRoot.GetComponent<Animator>();
			}

			if (_weaponModel != null)
			{
				_modelRenderer = _weaponModel.GetComponent<SpriteRenderer>();
			}

			_unitAnimator = this.GetComponent<UnitAnimator>();
		}

		// 무기를 facing 방향으로 돌린다. 0도가 정면(로컬 -x, 원본 스프라이트가 좌향)이다.
		//
		// 가로 성분은 부호를 버리고 항상 정면으로 본다 — 몸 반전은 평활·데드밴드로 0.1초쯤 늦게 따라오는데,
		// 그 사이 facing 이 몸 뒤쪽을 가리켜도 무기는 정면 기준 같은 높이를 유지하다가 반전과 함께 넘어간다.
		// (예전처럼 ±90도로 자르면 지연 구간에 무기가 수직 위/아래로 튀었다가 돌아온다.)
		// 반전이 제때 끝난 상태에서는 -facing.x * 스케일부호 와 |facing.x| 가 같으므로 결과도 같다.
		public void Tick(Vector2 facing, float dt)
		{
			if (_weaponAim == null || _flipRoot == null)
			{
				return;
			}

			refreshController();
			updateAttackState(dt);

			if (facing.sqrMagnitude < 1E-06f)
			{
				return;
			}

			// x 가 0 이상이라 결과는 항상 -90 ~ 90 이다.
			float angle = Mathf.Atan2(-facing.y, Mathf.Abs(facing.x)) * Mathf.Rad2Deg;

			// 정지·직진·시전 고정 중에는 각이 그대로다 — 방향을 틀 때만 아래를 수행한다.
			if (Mathf.Abs(angle - _lastAngle) < AngleEpsilon)
			{
				return;
			}

			_lastAngle = angle;

			// 로컬 값이라 좌우 반전은 _flipRoot 가 처리한다.
			_weaponAim.localRotation = Quaternion.Euler(0f, 0f, angle);

			if (_hasAimParam == true)
			{
				_animator.SetFloat(HashAimAngle, angle);

				// 이 Tick 은 LateUpdate(UnitManager) 라 Animator 평가가 이미 끝났다 — 그대로 두면 Motion 의 각도별 자세가
				// 한 프레임 늦게 따라와 WeaponAim 회전과 어긋난다. 각이 바뀐 프레임에만 0초로 한 번 더 평가해 맞춘다.
				_animator.Update(0f);
			}
		}

		// 무기 그룹 순서 — Hero.ManualTick 이 base(UnitAnimator.UpdateSorting) 뒤에 매 프레임 부른다.
		// 몸 뒤면 몸 바로 아래(-1), 몸 앞이면 사거리만큼 앞. 바뀔 때만 쓴다.
		public void SyncSorting()
		{
			if (_weaponGroup == null || _unitAnimator == null || _modelRenderer == null)
			{
				return;
			}

			int body = _unitAnimator.SortingOrder;
			if (body == int.MinValue)
			{
				return;
			}

			int hint = _modelRenderer.sortingOrder;
			bool front;
			if (hint > 0)
			{
				front = true;
			}
			else if (hint < 0)
			{
				front = false;
			}
			else
			{
				// 조준각을 아직 못 받았으면(NaN) 앞으로 둔다
				front = (_lastAngle > BackAngle) == false;
			}

			int order = front ? body + _frontSortOffset : body - 1;

			// sortingOrder 는 int16 이다 — 맵 끝에서 더한 값이 넘치지 않게 자른다
			order = Mathf.Clamp(order, short.MinValue, short.MaxValue);
			if (order == _lastWeaponOrder)
			{
				return;
			}

			_lastWeaponOrder = order;
			_weaponGroup.sortingOrder = order;
		}

		// 참조 비교 한 번 — 컨트롤러가 바뀐 프레임에만 파라미터 유무를 다시 보고 조준 캐시를 버린다.
		private void refreshController()
		{
			if (_animator == null || _animator.runtimeAnimatorController == _lastController)
			{
				return;
			}

			// 초기화 전 Animator 는 parameters 가 비어 있다 — 그때 캐시하면 AimAngle 이 없다고 굳어 버린다. 다음 틱에 다시 본다.
			if (_animator.isInitialized == false)
			{
				return;
			}

			_lastController = _animator.runtimeAnimatorController;
			_lastAngle = float.NaN;
			_hasAimParam = false;

			// 컨트롤러 교체는 레이어 가중치도 기본값으로 되돌린다 — 다시 쓰게 한다
			_weaponLayer = _animator.GetLayerIndex(WeaponLayerName);
			_swayLayer = _animator.GetLayerIndex(SwayLayerName);
			_swayWeight = float.NaN;
			_attackStateDirty = true;
			_hasAttackingParam = false;

			AnimatorControllerParameter[] parameters = _animator.parameters;
			for (int i = 0; i < parameters.Length; i++)
			{
				if (parameters[i].nameHash == HashAimAngle)
				{
					_hasAimParam = true;
				}
				else if (parameters[i].nameHash == HashWeaponAttacking)
				{
					_hasAttackingParam = true;
				}
			}
		}

		// 공격 여부를 Weapon 레이어 상태에서 읽어 흔들림·오프셋 레이어에 알린다.
		// 전이 중이면 넘어가는 상태 기준 — 공격 종료 전이가 시작되면 바로 되살리기 시작한다.
		// 흔들림은 끌 때 즉시(공격 준비자세에 섞이지 않게), 켤 때 _swayRecoverTime 동안 올린다.
		private void updateAttackState(float dt)
		{
			if (_weaponLayer < 0)
			{
				return;
			}

			AnimatorStateInfo state;
			if (_animator.IsInTransition(_weaponLayer) == true)
			{
				state = _animator.GetNextAnimatorStateInfo(_weaponLayer);
			}
			else
			{
				state = _animator.GetCurrentAnimatorStateInfo(_weaponLayer);
			}

			bool attacking = state.tagHash == HashAttackTag;
			bool started = false;
			if (attacking != _isAttacking || _attackStateDirty == true)
			{
				started = attacking == true && _isAttacking == false;
				_isAttacking = attacking;
				_attackStateDirty = false;
				if (_hasAttackingParam == true)
				{
					_animator.SetBool(HashWeaponAttacking, attacking);
				}
			}

			if (_swayLayer >= 0)
			{
				float target = attacking ? 0f : 1f;
				float weight = target;
				if (float.IsNaN(_swayWeight) == false && target > _swayWeight && _swayRecoverTime > 0f)
				{
					weight = Mathf.MoveTowards(_swayWeight, target, dt / _swayRecoverTime);
				}

				if (weight != _swayWeight)
				{
					_swayWeight = weight;
					_animator.SetLayerWeight(_swayLayer, weight);
				}
			}

			// 이 Tick 은 Animator 평가 뒤라 그대로 두면 공격 첫 프레임이 흔들림·이동 오프셋을 단 채 그려진다.
			// 공격이 시작된 프레임에만 0초로 한 번 더 평가해 맞춘다.
			if (started == true)
			{
				_animator.Update(0f);
			}
		}

		// 플레이 중 인스펙터를 고치면 같은 각에서도 다시 반영되게 캐시를 버린다.
		private void OnValidate()
		{
			_lastAngle = float.NaN;
		}
	}
}
