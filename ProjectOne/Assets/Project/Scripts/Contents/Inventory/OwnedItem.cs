namespace ProjectOne.UserData
{
	// 스택 아이템 1종(클라 런타임) — 재료·소모품·수집품.
	// 장비는 인스턴스 단위이므로 여기 들어오지 않는다 (ProjectOne.Items.EquipmentInstance).
	public sealed class OwnedItem
	{
		public int itemId;
		public int count;
	}
}
