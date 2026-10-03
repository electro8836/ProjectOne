using EDT;

namespace ProjectOne.Shared
{
	// 수집품(ItemMainCategory.Collection) 판정 — 클라·서버 공용.
	//
	// 펫·코스튬은 보상 표에서 일반 아이템(RewardType.Item)으로 나오고, 지급 단계에서만 인벤토리 대신 보유로 간다.
	// 펫·코스튬 테이블 ID 는 아이템 ID 와 같다(코스튬 10401xxxxx, 펫 10402xxxxx).
	public static class CollectionRules
	{
		public static bool IsCollection(int itemId)
		{
			Table_Item.Row row = Table_Item.Get(itemId);
			return row != null && row.MainCategory == ItemMainCategory.Collection;
		}

		public static bool IsPet(int itemId)
		{
			Table_Item.Row row = Table_Item.Get(itemId);
			return row != null && row.MainCategory == ItemMainCategory.Collection && row.SubCategory == ItemSubCategory.Pet;
		}

		public static bool IsCostume(int itemId)
		{
			Table_Item.Row row = Table_Item.Get(itemId);
			return row != null && row.MainCategory == ItemMainCategory.Collection && row.SubCategory == ItemSubCategory.Costume;
		}
	}
}
