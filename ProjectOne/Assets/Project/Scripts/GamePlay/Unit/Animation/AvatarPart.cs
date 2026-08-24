namespace ProjectOne.Unit
{
	// Prefab_Hero 의 SpriteRenderer 파츠를 지목하는 키.
	//
	// 오브젝트 이름을 키로 쓸 수 없어 enum 으로 잡는다 — 계층에 Front 가 4개, Back 이 3개 있고
	// '12_Helmet2 ' 는 이름 끝에 공백이 있다. 실제 바인딩은 HeroAvatar 가 인스펙터로 들고 있다.
	//
	// 멤버 추가는 반드시 맨 끝에 한다. ScriptableObject 가 이 값을 int 로 직렬화하므로
	// 중간에 끼워 넣으면 이미 만든 세트 에셋의 파츠 지정이 전부 밀린다.
	//
	// 주석의 괄호 숫자는 프리팹의 sortingOrder 다 (건드리지 않는다).
	public enum AvatarPart
	{
		None = 0,

		// 손
		WeaponL,		// L_Weapon        (19)
		WeaponR,		// R_Weapon        (-15)
		ShieldL,		// L_Shield        (25)
		ShieldR,		// R_Shield        (-21)

		// 머리
		Hair,			// 7_Hair          (6)
		FaceHair,		// 6_FaceHair      (5)
		Helmet1,		// 11_Helmet1      (11)
		Helmet2,		// 12_Helmet2      (12)  이름 끝에 공백이 있는 그 오브젝트다
		Head,			// 5_Head          (5)

		// 몸통
		Body,			// Body            (0)
		ClothBody,		// ClothBody       (1)
		BodyArmor,		// BodyArmor       (2)
		Cape,			// Back            (-100)  등 뒤(망토/백팩)

		// 팔
		ArmL,			// 20_L_Arm        (20)
		ArmR,			// -20_R_Arm       (-20)
		ClothArmL,		// 21_LCArm        (21)
		ClothArmR,		// -19_RCArm       (-19)
		ShoulderL,		// 25_L_Shoulder   (25)
		ShoulderR,		// -15_R_Shoulder  (-15)

		// 다리
		FootL,			// _3L_Foot        (-3)
		FootR,			// _12R_Foot       (-12)
		ClothLegL,		// _2L_Cloth       (-2)
		ClothLegR,		// _11R_Cloth      (-11)

		// 눈 — 표정용이라 코스튬 대상은 아니다. 나중에 중간 삽입을 피하려고 지금 자리를 잡아 둔다.
		EyeLBack,		// P_LEye/PivotBack/Back      (6)
		EyeLFront,		// P_LEye/PivotFront/Front    (6)
		EyeRBack,		// P_REye/PivotBack/Back      (6)
		EyeRFront,		// P_REye/PivotFront/Front    (7)
		EyeLClose,		// P_LClose/PivotFront/Front  (6)   기본 비활성
		EyeRClose,		// P_RClose/PivotFront/Front  (7)   기본 비활성

		Shadow,			// Shadow          (-50)  코스튬 대상 아님 — 프리팹 스프라이트 고정
	}
}
