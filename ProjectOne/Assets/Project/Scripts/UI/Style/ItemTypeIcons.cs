using EDT;

namespace ProjectOne.UI
{
	// 아이템 분류 배지의 아이콘 주소. 아이콘은 Atlas_Icon 에 있어 AtlasManager 로 동기 조회한다.
	// 장비는 장착 부위(EquipSlotTypes)별 아이콘(TypeIcon_<부위>)을 쓰고, 소모품은 하나로 묶는다.
	// 아이콘 그림은 Tools/pixelui/generate_violet_ui.py(type_icons)가 만든다.
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

			Table_Equipment.Row equipment = Table_Equipment.Get(row.ID);
			if (equipment == null)
			{
				return string.Empty;
			}

			switch (equipment.EquipSlotType)
			{
				case EquipSlotTypes.Weapon:		return "TypeIcon_Weapon";
				case EquipSlotTypes.Helmet:		return "TypeIcon_Helmet";
				case EquipSlotTypes.Armor:		return "TypeIcon_Armor";
				case EquipSlotTypes.Gloves:		return "TypeIcon_Gloves";
				case EquipSlotTypes.Boots:		return "TypeIcon_Boots";
				case EquipSlotTypes.Ring:		return "TypeIcon_Ring";
				case EquipSlotTypes.Amulet:		return "TypeIcon_Amulet";
				case EquipSlotTypes.Relic:		return "TypeIcon_Relic";
			}

			return string.Empty;
		}
	}
}
