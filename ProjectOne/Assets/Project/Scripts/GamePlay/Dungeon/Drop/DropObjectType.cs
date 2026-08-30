namespace ProjectOne.Dungeon
{
	// 바닥에 떨어지는 드랍 오브젝트의 종류.
	// 표시·획득 처리가 게임 내내 고정된 규칙이라 테이블이 아닌 코드가 소유한다.
	// (구 Table_DropObject 의 enum 컬럼을 코드로 옮긴 것)
	public enum DropObjectType
	{
		None,
		HealOrb,
		BuffRune,
		Item,
	}
}
