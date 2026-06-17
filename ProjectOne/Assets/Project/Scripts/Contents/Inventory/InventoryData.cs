using System.Collections.Generic;

namespace ProjectOne.ServerData
{
	// 보유 아이템 1종 — count >= 1 이면 사용 가능, 합성 시 소모된다. enhanceLevel 은 0~15 보관(스탯 미반영).
	[System.Serializable]
	public class OwnedItem
	{
		public int itemId;
		public int count;
		public int enhanceLevel;
	}

	// 아이템정보(인벤토리) 도메인 데이터
	[System.Serializable]
	public class InventoryData
	{
		public List<OwnedItem> items = new List<OwnedItem>();
	}
}
