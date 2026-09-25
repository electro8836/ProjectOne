using EDT;
using UnityEngine;
using ProjectOne.Avatar;
using ProjectOne.Costumes;
using ProjectOne.Items;
using ProjectOne.Mastery;
using ProjectOne.Unit;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 캐릭터 프리뷰용 더미 히어로(Prefab_HeroPreview)의 루트.
	//
	// 인게임 히어로를 그대로 찍을 수 없어서 존재한다 — 실제 히어로는 이동·공격 중이라
	// 포즈가 제각각이고, 정렬 order 도 월드 Y 를 따라 매 프레임 바뀐다.
	// 여기서는 Hero/UnitMover/UnitAnimator 를 전부 걷어낸 껍데기만 두고 IDLE 만 돌린다
	// (AC_Root 의 기본 상태가 IDLE 이라 파라미터를 건드리지 않으면 그대로 루프한다).
	public class CharacterPreviewRig : MonoBehaviour
	{
		[SerializeField] private HeroAvatar _avatar;
		[SerializeField] private Animator _animator;
		[SerializeField] private Camera _camera;

		// 프리팹에 직렬화된 기본 컨트롤러(AOC_Hero_Default). 무기를 벗으면 여기로 돌아온다.
		private RuntimeAnimatorController _baseController;

		// 입어보기로 덮어쓴 코스튬 ID. -1 이면 실제 착용을 따른다 (0 은 "벗은 상태"라는 뜻이라 쓸 수 없다).
		private int _previewWeaponId = -1;
		private int _previewBodyId = -1;

		// 내 Account 대신 그릴 외형(다른 플레이어). _useExternal 이 false 면 내 착용을 따른다.
		private bool _useExternal;
		private int _externalWeaponCostumeId;
		private int _externalBodyCostumeId;
		private EquipmentInstance _externalWeapon;

		public Camera Camera
		{
			get { return _camera; }
		}

		private void Awake()
		{
			if (_animator != null)
			{
				_baseController = _animator.runtimeAnimatorController;
			}
		}

		// 입어보기 대상을 지정한다. -1 을 넘기면 그 부위는 실제 착용으로 되돌아간다.
		// 반영은 Refresh 에서 하므로 호출자가 이어서 Refresh 를 부른다.
		public void SetPreviewCostume(int weaponCostumeId, int bodyCostumeId)
		{
			_previewWeaponId = weaponCostumeId;
			_previewBodyId = bodyCostumeId;
		}

		// 내 Account 대신 넘겨받은 착용으로 그린다(다른 플레이어 정보). 반영은 Refresh 에서 한다.
		public void SetExternal(int weaponCostumeId, int bodyCostumeId, EquipmentInstance weapon)
		{
			_useExternal = true;
			_externalWeaponCostumeId = weaponCostumeId;
			_externalBodyCostumeId = bodyCostumeId;
			_externalWeapon = weapon;
		}

		// 착용 장비·코스튬을 외형에 반영한다. 창이 열릴 때와 장착이 바뀔 때마다 호출된다.
		public void Refresh()
		{
			applyAvatar();
			applyAnimController();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// HeroAvatarAspect.ApplyTo 와 같은 순서다 — 바디가 몸 27파츠를 완전히 지정하고,
		// 무기를 나중에 얹어 코스튬 무기가 장비 무기를 덮어쓰게 한다.
		// 규칙 자체는 HeroAvatarAspect 의 Resolve 를 그대로 빌려 쓴다 — 인게임과 어긋나면 안 된다.
		private void applyAvatar()
		{
			if (_avatar == null)
			{
				Debug.LogError($"[CharacterPreviewRig] HeroAvatar 가 비어 있습니다 ({this.name})");
				return;
			}

			_avatar.ResetAll();
			_avatar.ApplyBodySet(resolveBodySet());
			_avatar.ApplyWeaponSet(resolveWeaponSet());
		}

		private AvatarBodySet resolveBodySet()
		{
			if (_previewBodyId < 0)
			{
				if (_useExternal == true)
				{
					return HeroAvatarAspect.ResolveBodySet(_externalBodyCostumeId);
				}

				return HeroAvatarAspect.ResolveBodySet();
			}

			Table_Costume.Row row = CostumeCatalog.Get(_previewBodyId);
			if (row == null || row.CostumeType != CostumeType.Body)
			{
				// 벗어보기(0)이거나 데이터가 어긋났으면 기본 바디로 — 인게임의 미착용과 같은 모습이다.
				row = CostumeCatalog.DefaultBody;
			}

			if (row == null)
			{
				return null;
			}

			return AvatarCatalog.GetBodySet(row.SetAddress);
		}

		// 여기서만 인게임 규칙과 일부러 어긋난다 — CostumeCatalog.CanShowWeapon 의 직업 제한을 보지 않는다.
		// 입어보기는 "지금 들 수 있는가"가 아니라 "이 코스튬이 어떻게 생겼는가"를 보여 주는 기능이라,
		// 제한에 걸려 아무 변화도 없으면 버튼이 고장난 것처럼 보인다.
		private AvatarWeaponSet resolveWeaponSet()
		{
			if (_previewWeaponId < 0)
			{
				return resolveWornWeaponSet();
			}

			Table_Costume.Row row = CostumeCatalog.Get(_previewWeaponId);
			if (row == null || row.CostumeType != CostumeType.Weapon)
			{
				// 벗어보기 — 장비 무기가 다시 보이도록 실제 착용 규칙으로 돌아간다.
				return resolveWornWeaponSet();
			}

			return AvatarCatalog.GetWeaponSet(row.SetAddress);
		}

		// 실제 착용 규칙의 무기 — 외형 모드면 넘겨받은 착용, 아니면 내 Account.
		private AvatarWeaponSet resolveWornWeaponSet()
		{
			if (_useExternal == true)
			{
				return HeroAvatarAspect.ResolveWeaponSet(_externalWeaponCostumeId, _externalWeapon);
			}

			return HeroAvatarAspect.ResolveWeaponSet();
		}

		// 무기별 오버라이드 컨트롤러. AC_Root 의 IDLE 클립도 AOC 가 갈아끼우는 대상이라,
		// 이 교체가 없으면 석궁을 들어도 맨손 IDLE 포즈가 나온다.
		private void applyAnimController()
		{
			if (_animator == null)
			{
				return;
			}

			Table_WeaponMastery.Row mastery;
			if (_useExternal == true)
			{
				mastery = MasteryCatalog.GetByWeaponType(HeroAvatarAspect.GetWeaponType(_externalWeapon));
			}
			else
			{
				MasteryBook book = Account.Instance.Mastery;
				mastery = (book != null) ? book.CurrentMastery : null;
			}

			if (mastery == null)
			{
				// 무기 미착용 — 기본 컨트롤러로 되돌린다.
				_animator.runtimeAnimatorController = _baseController;
				return;
			}

			RuntimeAnimatorController controller = MasteryCatalog.GetAnimController(mastery.AnimControllerName);
			if (controller == null)
			{
				// 이름이 비었거나 프리로드 누락 — GetAnimController 가 이미 로그를 남겼다.
				_animator.runtimeAnimatorController = _baseController;
				return;
			}

			_animator.runtimeAnimatorController = controller;
		}
	}
}
