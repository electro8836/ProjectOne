using UnityEngine;

namespace ProjectOne.Unit
{
	// 바디 코스튬 한 벌의 외형. 몸 전체를 완전히 지정한다 — **비워 둔 필드는 그 파츠를 감춘다.**
	//
	// 무기(WeaponL/WeaponR)는 여기 없다. 장비 무기와 코스튬 무기가 AvatarWeaponSet 으로 담당하고,
	// 무기셋이 바디셋보다 나중에 적용되어 손에 든 것을 결정한다.
	// 그림자(Shadow)도 없다 — 코스튬과 무관하게 프리팹 스프라이트를 그대로 쓴다.
	//
	// 새 코스튬은 기본 코스튬 세트를 복제해 바꿀 부분만 고치는 것이 편하다 —
	// 완전 지정이라 눈·그림자·맨살까지 채워져 있어야 사라지지 않는다.
	//
	// 배치 위치: Assets/Project/Data/ScriptableObject/Avatar/
	// 주소는 파일명 그대로 AddressableAutoMarker 가 자동 등록한다.
	[CreateAssetMenu(fileName = "AvatarBodySet", menuName = "ProjectOne/Avatar/Body Set")]
	public class AvatarBodySet : ScriptableObject
	{
		// 손 — 방패는 아직 장비가 아니라 바디셋이 들고 있다.
		// 방패가 장비가 되면 AvatarWeaponSet 을 4슬롯으로 늘리고 여기서 빼야 한다.
		[SerializeField] private Sprite _shieldL;
		[SerializeField] private Sprite _shieldR;

		// 머리
		[SerializeField] private Sprite _hair;
		[SerializeField] private Sprite _faceHair;
		[SerializeField] private Sprite _helmet1;
		[SerializeField] private Sprite _helmet2;
		[SerializeField] private Sprite _head;

		// 몸통
		[SerializeField] private Sprite _body;
		[SerializeField] private Sprite _clothBody;
		[SerializeField] private Sprite _bodyArmor;
		[SerializeField] private Sprite _cape;

		// 팔
		[SerializeField] private Sprite _armL;
		[SerializeField] private Sprite _armR;
		[SerializeField] private Sprite _clothArmL;
		[SerializeField] private Sprite _clothArmR;
		[SerializeField] private Sprite _shoulderL;
		[SerializeField] private Sprite _shoulderR;

		// 다리
		[SerializeField] private Sprite _footL;
		[SerializeField] private Sprite _footR;
		[SerializeField] private Sprite _clothLegL;
		[SerializeField] private Sprite _clothLegR;

		// 눈
		[SerializeField] private Sprite _eyeLBack;
		[SerializeField] private Sprite _eyeLFront;
		[SerializeField] private Sprite _eyeRBack;
		[SerializeField] private Sprite _eyeRFront;
		[SerializeField] private Sprite _eyeLClose;
		[SerializeField] private Sprite _eyeRClose;

		// 파츠 하나의 스프라이트. null 이면 그 파츠를 감춘다는 뜻이다.
		// 무기와 None 은 이 세트의 관할이 아니므로 항상 null 이 돌아온다.
		public Sprite Get(AvatarPart part)
		{
			switch (part)
			{
				case AvatarPart.ShieldL:    return _shieldL;
				case AvatarPart.ShieldR:    return _shieldR;

				case AvatarPart.Hair:       return _hair;
				case AvatarPart.FaceHair:   return _faceHair;
				case AvatarPart.Helmet1:    return _helmet1;
				case AvatarPart.Helmet2:    return _helmet2;
				case AvatarPart.Head:       return _head;

				case AvatarPart.Body:       return _body;
				case AvatarPart.ClothBody:  return _clothBody;
				case AvatarPart.BodyArmor:  return _bodyArmor;
				case AvatarPart.Cape:       return _cape;

				case AvatarPart.ArmL:       return _armL;
				case AvatarPart.ArmR:       return _armR;
				case AvatarPart.ClothArmL:  return _clothArmL;
				case AvatarPart.ClothArmR:  return _clothArmR;
				case AvatarPart.ShoulderL:  return _shoulderL;
				case AvatarPart.ShoulderR:  return _shoulderR;

				case AvatarPart.FootL:      return _footL;
				case AvatarPart.FootR:      return _footR;
				case AvatarPart.ClothLegL:  return _clothLegL;
				case AvatarPart.ClothLegR:  return _clothLegR;

				case AvatarPart.EyeLBack:   return _eyeLBack;
				case AvatarPart.EyeLFront:  return _eyeLFront;
				case AvatarPart.EyeRBack:   return _eyeRBack;
				case AvatarPart.EyeRFront:  return _eyeRFront;
				case AvatarPart.EyeLClose:  return _eyeLClose;
				case AvatarPart.EyeRClose:  return _eyeRClose;
			}

			return null;
		}

		// 이 세트가 관할하는 파츠인가 — 관할 밖은 바디셋 적용에서 건너뛴다.
		// 무기 2파츠는 무기셋이 결정하고, 그림자는 코스튬과 무관하게 항상 고정이다.
		public static bool Covers(AvatarPart part)
		{
			switch (part)
			{
				case AvatarPart.None:
				case AvatarPart.WeaponL:
				case AvatarPart.WeaponR:
				case AvatarPart.Shadow:
					return false;
			}

			return true;
		}
	}
}
