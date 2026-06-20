using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 보유 아이템 1종(직렬화 DTO) — enhanceLevel 은 0~15 보관(스탯 미반영).
	[System.Serializable]
	public class OwnedItemDto
	{
		public int itemId;
		public int count;
		public int enhanceLevel;
	}

	// 인벤토리 직렬화 DTO — 서버-클라 공유 영속 스키마. 클라는 Inventory 로 변환해 사용한다.
	[System.Serializable]
	public class InventoryDto
	{
		public List<OwnedItemDto> items = new List<OwnedItemDto>();
	}
}
