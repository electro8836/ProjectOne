using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectOne.Unit
{
	// 히어로 무기 조준·배치. 손에 달지 않고 손이 있을 자리(WeaponAim)에 두고 facing 방향으로 돌린다.
	//
	// 계층 역할: WeaponAim(중심·조준 회전, 이 컴포넌트) → Motion(AC_Hero Weapon 레이어) → Model(무기 스프라이트, HeroAvatar)
	// Motion 의 조준각별 Idle 자세는 블렌드 트리가 AimAngle 파라미터로 고른다. 이 컴포넌트는 그 값을 넘기기만 한다.
	// 공격 중에도 WeaponAim 은 조준각을 유지한다 — 같은 공격 모션이 바라보는 방향으로 나간다.
	// WeaponAim·Model 은 애니메이션 클립이 키를 잡으면 안 된다 — Weapon 레이어 마스크도 Motion 만 연다.
	//
	// Hero.ManualTick 이 base(UnitAnimator 좌우 반전) 직후 Tick 을 부른다. 반전 결과를 읽어야 각이 맞는다.
	public class HeroWeapon : MonoBehaviour
	{
		private static readonly int HashAimAngle = Animator.StringToHash("AimAngle");

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

		// 앞뒤 판정 — 공격 클립이 Model 의 sortingOrder 를 몸 앞(30)·뒤(-99)로 키를 잡는다
		private SpriteRenderer _modelRenderer;

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
		public void Tick(Vector2 facing)
		{
			if (_weaponAim == null || _flipRoot == null)
			{
				return;
			}

			refreshController();

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

			bool front = _modelRenderer.sortingOrder >= 0;
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

			AnimatorControllerParameter[] parameters = _animator.parameters;
			for (int i = 0; i < parameters.Length; i++)
			{
				if (parameters[i].nameHash == HashAimAngle)
				{
					_hasAimParam = true;
					break;
				}
			}
		}

		// 플레이 중 인스펙터를 고치면 같은 각에서도 다시 반영되게 캐시를 버린다.
		private void OnValidate()
		{
			_lastAngle = float.NaN;
		}
	}
}
