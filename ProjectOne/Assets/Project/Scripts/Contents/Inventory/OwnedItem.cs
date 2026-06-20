namespace ProjectOne.UserData
{
	// 보유 아이템 1종(클라 런타임). 직렬화 DTO(OwnedItemDto)와 분리 — 클라 전용 필드는 여기에 추가한다.
	public sealed class OwnedItem
	{
		public int itemId;
		public int count;
		public int enhanceLevel;
	}
}
