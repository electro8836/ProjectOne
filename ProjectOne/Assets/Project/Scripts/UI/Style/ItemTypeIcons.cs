using EDT;

namespace ProjectOne.UI
{
	// 아이템 분류 배지의 아이콘 주소. 아이콘은 Atlas_Icon 에 있어 AtlasManager 로 동기 조회한다.
	// 장착 부위(EquipSlotTypes)가 아니라 아이템 분류를 나타내므로 SubCategory 축을 쓴다
	// — 그래야 장착 부위가 없는 소모품까지 한 함수로 덮인다.
	public static class ItemTypeIcons
	{
		// 매칭되는 분류가 없으면 빈 문자열 — 호출자가 배지를 감춘다.
		public static string Get(Table_Item.Row row)
		{
			if (row == null)
			{
				return string.Empty;
			}

			// 소모품은 세부 분류(Usable/Box/SkillBook)를 구분하지 않고 하나로 묶는다.
			if (row.MainCategory == ItemMainCategory.Consumable)
			{
				return "TypeIcon_Consumable";
			}

			if (row.MainCategory != ItemMainCategory.Equipment)
			{
				return string.Empty;
			}

			switch (row.SubCategory)
			{
				case ItemSubCategory.Weapon:		return "TypeIcon_Weapon";
				case ItemSubCategory.Armor:			return "TypeIcon_Armor";
				case ItemSubCategory.Accessory:	return "TypeIcon_Accessory";
				case ItemSubCategory.Relic:			return "TypeIcon_Relic";
			}

			return string.Empty;
		}
	}
}
