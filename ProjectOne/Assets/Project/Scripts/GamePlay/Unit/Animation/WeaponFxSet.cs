using UnityEngine;

namespace ProjectOne.Unit
{
	// 무기 FX(도검 궤적·총구 섬광) 스프라이트 묶음. 장비 행(Equipment.WeaponFX)이 주소로 고른다.
	//
	// 공격 클립은 스프라이트를 직접 잡지 않고 WeaponFxFrame 파라미터로 이 배열의 인덱스만 키로 잡는다(-1 = 끔).
	// 타이밍은 클립, 생김새는 장비가 정한다 — 같은 무기 종류의 다른 장비가 다른 궤적을 낼 수 있다.
	// 콤보 단계별 프레임을 이어 붙인다(예: 1타 0~9, 2타 10~19). 같은 무기 종류의 세트는 프레임 수가 같아야 한다.
	[CreateAssetMenu(fileName = "WeaponFx_", menuName = "ProjectOne/Avatar/WeaponFxSet")]
	public class WeaponFxSet : ScriptableObject
	{
		[SerializeField] private Sprite[] _frames = new Sprite[0];

		// 범위 밖(-1 포함)이면 null — FX 를 감춘다.
		public Sprite Get(int index)
		{
			if (index < 0 || index >= _frames.Length)
			{
				return null;
			}

			return _frames[index];
		}
	}
}
