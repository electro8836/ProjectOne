using UnityEngine;

namespace ProjectOne.Unit
{
	// 무기 한 자루의 외형. 왼손·오른손 2슬롯 고정이다 —
	// 쌍검은 둘 다 채우고, 룬소드처럼 한손 무기는 한쪽만 채운다(나머지는 비워 두면 손이 빈다).
	//
	// 배치 위치: Assets/Project/Data/ScriptableObject/Avatar/
	// 주소는 파일명 그대로 AddressableAutoMarker 가 자동 등록한다.
	// 스프라이트는 이 에셋이 직접 참조하므로 개별 어드레서블 등록이 필요 없다.
	[CreateAssetMenu(fileName = "AvatarWeaponSet", menuName = "ProjectOne/Avatar/Weapon Set")]
	public class AvatarWeaponSet : ScriptableObject
	{
		[SerializeField] private Sprite _left;

		[SerializeField] private Sprite _right;

		public Sprite Left => _left;

		public Sprite Right => _right;
	}
}
