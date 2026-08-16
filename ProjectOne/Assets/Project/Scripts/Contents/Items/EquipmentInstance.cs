using EDT;

namespace ProjectOne.Items
{
	// 장비 인스턴스 — 저장되는 동적 데이터는 이것이 전부다 (아이템 설계 4장).
	//
	// 옵션 수치는 저장하지 않는다. (ItemID, Grade, Level, Purity, Quality) 로 매번 재계산한다.
	// 그래서 승급은 Grade 를 한 단계 올리는 것만으로 끝나고 레벨·순도·품질이 자연히 보존된다.
	public sealed class EquipmentInstance
	{
		public long uid;
		public int itemId;
		public ItemGradeType grade;
		public int level;
		public EquipPurity purity;
		public int quality;

		// 착용 중인 슬롯. None 이면 미착용.
		public EquipSlotTypes equippedSlot;

		public bool IsEquipped
		{
			get { return equippedSlot != EquipSlotTypes.None; }
		}

		// 이 인스턴스의 정적 장비 정보. 데이터가 없으면 null.
		public Table_Equipment.Row Equipment
		{
			get { return Table_Equipment.Get(itemId); }
		}

		public Table_Item.Row Item
		{
			get { return Table_Item.Get(itemId); }
		}
	}
}
