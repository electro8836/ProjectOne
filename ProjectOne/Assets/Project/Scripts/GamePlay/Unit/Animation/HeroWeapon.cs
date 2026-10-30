using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectOne.Unit
{
	// 히어로 무기 조준·배치. 손에 달지 않고 손이 있을 자리(WeaponAim)에 두고 facing 방향으로 돌린다.
	//
	// 계층 역할: WeaponAim(중심·조준 회전, 이 컴포넌트 / 위치 = 흔들림, WeaponSway 레이어)
	//   → Offset(조준각별 위치, WeaponOffset 레이어) → Motion(자세·공격 모션, Weapon 레이어) → Model(무기 스프라이트, HeroAvatar)
	// 조준각별 오프셋은 블렌드 트리가 AimAngle 파라미터로 고른다. 이 컴포넌트는 그 값을 넘기기만 한다.
	// 공격 모션은 각도 하나로 끝난다 — 휘두르는 구간은 클립이 WeaponOffsetOff 를 1 로 키를 잡아 오프셋을 끄고
	// WeaponAim 기준 절대 위치로 움직인다(판정·궤적과 일치). 반동처럼 손에서의 상대 움직임은 0 으로 두어 오프셋을 유지한다.
	// 공격 중에도 WeaponAim 은 조준각을 유지한다 — 같은 공격 모션이 바라보는 방향으로 나간다.
	// WeaponAim 회전은 클립이 키를 잡으면 안 된다(이 컴포넌트가 쓴다). 위치는 흔들림 레이어만 쓰고 공격 중엔 0 이라
	// 공격 판정·무기 FX(WeaponFX) 기준점이 콜라이더 중심과 어긋나지 않는다.
	//
	// Hero.ManualTick 이 base(UnitAnimator 좌우 반전) 직후 Tick 을 부른다. 반전 결과를 읽어야 각이 맞는다.
	public class HeroWeapon : MonoBehaviour
	{
		private static readonly int HashAimAngle = Animator.StringToHash("AimAngle");

		// AC_Hero Weapon 레이어 공격 상태(WEAPON_ATTACK·_2·_3)의 태그
		private static readonly int HashAttackTag = Animator.StringToHash("Attack");

		// 총 공격 클립이 1 로 키를 잡는다 — 이 동안 무기 FX(총구 섬광)를 총구에 둔다. 도검은 0(조준 중심)
		private static readonly int HashWeaponMuzzle = Animator.StringToHash("WeaponMuzzle");

		// 공격 클립이 키를 잡는다 — 1 이면 오프셋을 끈다(오프셋 레이어 가중치 = 1 - 값). 공격 종료 전이 동안엔 1 → 0 으로 섞인다
		private static readonly int HashWeaponOffsetOff = Animator.StringToHash("WeaponOffsetOff");

		// 공격 클립이 키를 잡는다 — 1 이면 위쪽(BackAngle 이상)으로 공격할 때 무기 FX 를 무기 위·몸 아래에 그린다(태도)
		private static readonly int HashWeaponFxBehindUp = Animator.StringToHash("WeaponFxBehindUp");

		private const string WeaponLayerName = "Weapon";

		private const string OffsetLayerName = "WeaponOffset";

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
	// 무기 FX(WeaponFX — 도검 궤적·총구 섬광)는 자체 그룹(sortAtRoot, Effect 레이어)으로 이 그룹을 벗어나 몬스터·지형지물보다 항상 위에 그린다.
	// 예외: 클립이 WeaponFxBehindUp 을 켠 공격(태도)을 위쪽 BackAngle 이상으로 하면 그 공격 동안 무기 위·몸 아래(이 그룹 레이어의 몸 - 1)에 그린다.
		[SerializeField] private SortingGroup _weaponGroup;

		// 무기 FX(WeaponAim/WeaponFX). 총이면 공격 중 총구 위치로 옮긴다.
		[SerializeField] private Transform _weaponFx;

		// 무기 FX 정렬 그룹 — 평소엔 프리팹 값(Effect 레이어), 몸 뒤 모드면 무기 그룹 레이어의 무기와 몸 사이
		private SortingGroup _fxGroup;

		private int _fxLayerId;

		private int _fxOrder;

		private bool _hasFxBehindParam;

		// 이번 공격의 무기 FX 를 몸 뒤에 그리는지 — 공격이 시작될 때의 조준각으로 정해 그 공격 동안 고정한다(45도 근처 깜빡임 방지)
		private bool _fxBehind;

		// 지난 프레임 공격 상태의 진행도 — 같은 상태로 다시 들어간 공격(자기전이)을 알아낸다
		private float _lastAttackTime;

		private bool _lastFxBehind;

		private int _lastFxOrder = int.MinValue;

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

		private int _offsetLayer = -1;

		// 마지막으로 쓴 흔들림 레이어 가중치. 바뀔 때만 쓴다(NaN = 다시 써야 함).
		private float _swayWeight = float.NaN;

		// 마지막으로 쓴 오프셋 레이어 가중치. 바뀔 때만 쓴다(NaN = 다시 써야 함).
		private float _offsetWeight = float.NaN;

		private bool _hasOffsetOffParam;

		// 지난 프레임의 공격 여부 — 공격이 시작된 프레임을 알아낸다
		private bool _isAttacking;

		private bool _hasMuzzleParam;

		// 총구 캐시 — 무기 스프라이트가 바뀔 때만 다시 구한다(Model 로컬 좌표)
		private Sprite _muzzleSprite;

		private Vector3 _muzzleLocal;

		// 무기 FX 를 총구로 옮겨 둔 상태 — 끝나면 조준 중심(0)으로 한 번만 되돌린다
		private bool _fxAtMuzzle;

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

			if (_weaponFx != null)
			{
				_fxGroup = _weaponFx.GetComponent<SortingGroup>();
				if (_fxGroup != null)
				{
					_fxLayerId = _fxGroup.sortingLayerID;
					_fxOrder = _fxGroup.sortingOrder;
				}
			}
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

			// 이 Tick 은 Animator 평가 뒤라 여기서 바꾼 가중치는 다음 평가부터 반영된다 — 바뀐 프레임에만 0초로 한 번 더 평가해 맞춘다.
			bool swayStarted = updateSway(dt);
			bool offsetChanged = updateOffset();
			if (swayStarted == true || offsetChanged == true)
			{
				_animator.Update(0f);
			}

			updateFxBehind();
			updateWeaponFx();

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
		// 몸 뒤면 몸 - 2(몸 - 1 은 몸 뒤 모드 무기 FX 자리), 몸 앞이면 사거리만큼 앞. 바뀔 때만 쓴다.
		public void SyncSorting()
		{
			if (_unitAnimator == null)
			{
				return;
			}

			int body = _unitAnimator.SortingOrder;
			if (body == int.MinValue)
			{
				return;
			}

			SyncSorting(body);
		}

		// 몸 순서를 직접 받는 판 — UnitAnimator 가 없는 캐릭터 프리뷰가 고정 몸 순서로 부른다.
		public void SyncSorting(int body)
		{
			if (_weaponGroup == null || _modelRenderer == null)
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

			// 몸 뒤는 몸 - 2 — 몸 - 1 은 몸 뒤 모드의 무기 FX 자리(무기 위·몸 아래)
			int order = front ? body + _frontSortOffset : body - 2;

			// sortingOrder 는 int16 이다 — 맵 끝에서 더한 값이 넘치지 않게 자른다
			order = Mathf.Clamp(order, short.MinValue, short.MaxValue);
			if (order != _lastWeaponOrder)
			{
				_lastWeaponOrder = order;
				_weaponGroup.sortingOrder = order;
			}

			syncFxSorting(body);
		}

		// 무기 FX 정렬 — 몸 뒤 모드면 무기 그룹과 같은 레이어의 몸 - 1, 아니면 프리팹 값(Effect 레이어). 바뀔 때만 쓴다.
		private void syncFxSorting(int body)
		{
			if (_fxGroup == null)
			{
				return;
			}

			if (_fxBehind == true)
			{
				int order = Mathf.Clamp(body - 1, short.MinValue, short.MaxValue);
				if (_lastFxBehind == true && order == _lastFxOrder)
				{
					return;
				}

				_fxGroup.sortingLayerID = _weaponGroup.sortingLayerID;
				_fxGroup.sortingOrder = order;
				_lastFxOrder = order;
				_lastFxBehind = true;
				return;
			}

			if (_lastFxBehind == false)
			{
				return;
			}

			_fxGroup.sortingLayerID = _fxLayerId;
			_fxGroup.sortingOrder = _fxOrder;
			_lastFxBehind = false;
		}

		// 새 공격이 시작될 때 무기 FX 를 몸 뒤에 그릴지 정한다 — 클립 표식(WeaponFxBehindUp)이 켜져 있고 조준이 위쪽 BackAngle 이상일 때.
		// 그 공격 동안은 조준각이 바뀌어도 유지하고, 공격이 끝나면 끈다.
		// 같은 상태로 다시 들어가는 연속 공격(자기전이 0초)은 진행도가 되감기는 것으로 새 공격을 알아낸다.
		private void updateFxBehind()
		{
			if (_weaponLayer < 0 || _isAttacking == false)
			{
				_fxBehind = false;
				_lastAttackTime = 0f;
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

			float time = state.normalizedTime;
			bool newAttack = time < _lastAttackTime || _lastAttackTime <= 0f;
			_lastAttackTime = Mathf.Max(time, 1E-04f);
			if (newAttack == false)
			{
				return;
			}

			// 조준각은 아래가 + 라 위쪽 BackAngle 이상은 -BackAngle 이하다(NaN 이면 false)
			_fxBehind = _hasFxBehindParam == true && _animator.GetFloat(HashWeaponFxBehindUp) > 0.5f && _lastAngle <= -BackAngle;
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
			_offsetLayer = _animator.GetLayerIndex(OffsetLayerName);
			_swayWeight = float.NaN;
			_offsetWeight = float.NaN;
			_isAttacking = false;
			_hasMuzzleParam = false;
			_hasOffsetOffParam = false;
			_hasFxBehindParam = false;

			AnimatorControllerParameter[] parameters = _animator.parameters;
			for (int i = 0; i < parameters.Length; i++)
			{
				if (parameters[i].nameHash == HashAimAngle)
				{
					_hasAimParam = true;
				}
				else if (parameters[i].nameHash == HashWeaponMuzzle)
				{
					_hasMuzzleParam = true;
				}
				else if (parameters[i].nameHash == HashWeaponOffsetOff)
				{
					_hasOffsetOffParam = true;
				}
				else if (parameters[i].nameHash == HashWeaponFxBehindUp)
				{
					_hasFxBehindParam = true;
				}
			}
		}

		// 공격 중이면 흔들림을 끈다. 공격 여부는 Weapon 레이어 상태 태그로 판단하고,
		// 전이 중이면 넘어가는 상태 기준 — 공격 종료 전이가 시작되면 바로 되살리기 시작한다.
		// 끌 때는 즉시(공격 준비자세에 섞이지 않게), 켤 때는 _swayRecoverTime 동안 올린다.
		// 공격이 시작된 프레임이면 true — 그대로 두면 공격 첫 프레임이 흔들림을 단 채 그려지므로 호출자가 다시 평가한다.
		private bool updateSway(float dt)
		{
			if (_weaponLayer < 0)
			{
				return false;
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
			bool started = attacking == true && _isAttacking == false;
			_isAttacking = attacking;

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

			return started;
		}

		// 공격 클립이 키를 잡은 WeaponOffsetOff 로 오프셋 레이어를 끄고 켠다. 가중치가 바뀌었으면 true.
		private bool updateOffset()
		{
			if (_offsetLayer < 0 || _hasOffsetOffParam == false)
			{
				return false;
			}

			float weight = 1f - Mathf.Clamp01(_animator.GetFloat(HashWeaponOffsetOff));
			if (weight == _offsetWeight)
			{
				return false;
			}

			_offsetWeight = weight;
			_animator.SetLayerWeight(_offsetLayer, weight);
			return true;
		}

		// 총 공격 중이면 무기 FX 를 총구에 붙인다. 반동으로 총이 움직여도 섬광이 총구를 따라간다.
		// 클립이 WeaponFX 위치를 키로 잡지 않으므로(애니메이터가 덮어쓰지 않음) 여기서 쓴 값이 그대로 그려진다.
		private void updateWeaponFx()
		{
			if (_weaponFx == null || _weaponModel == null)
			{
				return;
			}

			bool muzzle = _hasMuzzleParam == true && _isAttacking == true && _animator.GetFloat(HashWeaponMuzzle) > 0.5f;
			if (muzzle == true && refreshMuzzle() == true)
			{
				_weaponFx.position = _weaponModel.TransformPoint(_muzzleLocal);
				_fxAtMuzzle = true;
				return;
			}

			if (_fxAtMuzzle == true)
			{
				_weaponFx.localPosition = Vector3.zero;
				_fxAtMuzzle = false;
			}
		}

		// 무기 스프라이트가 바뀌었으면 총구를 다시 구한다. 스프라이트가 없으면 false.
		// 무기 이미지 규칙: 총구가 위(맨 위 픽셀), 총열 중심이 캔버스 가로 가운데.
		// 가로는 캔버스 가운데를 쓴다 — 맨 위 줄은 가늠쇠 등이 붙어 있어 그 가운데가 총열 중심이 아니다.
		// 세로는 외곽선(타이트 메시) 꼭짓점의 가장 위다.
		private bool refreshMuzzle()
		{
			Sprite sprite = _modelRenderer.sprite;
			if (sprite == null)
			{
				_muzzleSprite = null;
				return false;
			}

			if (sprite == _muzzleSprite)
			{
				return true;
			}

			_muzzleSprite = sprite;
			Vector2[] vertices = sprite.vertices;
			float top = float.MinValue;
			for (int i = 0; i < vertices.Length; i++)
			{
				if (vertices[i].y > top)
				{
					top = vertices[i].y;
				}
			}

			float centerX = (sprite.rect.width * 0.5f - sprite.pivot.x) / sprite.pixelsPerUnit;
			_muzzleLocal = new Vector3(centerX, top, 0f);
			return true;
		}

		// 플레이 중 인스펙터를 고치면 같은 각에서도 다시 반영되게 캐시를 버린다.
		private void OnValidate()
		{
			_lastAngle = float.NaN;
		}
	}
}
