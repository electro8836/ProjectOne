namespace ProjectOne.Shared
{
	// 로드아웃 직렬화 DTO — 서버-클라 공유 스키마.
	// 캐릭터는 하나뿐이므로 캐릭터 목록/선택 개념이 없다. 레벨·경험치와 8슬롯 장착만 보관한다.
	// slots 는 EquipSlotTypes 의 정수값을 인덱스로 쓴다(0=None 자리는 사용하지 않음).
	// 값은 아이템 ID 가 아니라 **장비 인스턴스 UID** 다. 0 = 미장착.
	[System.Serializable]
	public class LoadoutDto
	{
		public const int SlotCount = 9;

		public int level = 1;
		public int exp;
		public long[] slots = new long[SlotCount];
	}
}
