using EDT;
using UnityEngine;
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
			_avatar.ApplyBodySet(HeroAvatarAspect.ResolveBodySet());
			_avatar.ApplyWeaponSet(HeroAvatarAspect.ResolveWeaponSet());
		}

		// 무기별 오버라이드 컨트롤러. AC_Root 의 IDLE 클립도 AOC 가 갈아끼우는 대상이라,
		// 이 교체가 없으면 석궁을 들어도 맨손 IDLE 포즈가 나온다.
		private void applyAnimController()
		{
			if (_animator == null)
			{
				return;
			}

			MasteryBook book = Account.Instance.Mastery;
			Table_WeaponMastery.Row mastery = (book != null) ? book.CurrentMastery : null;
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
