namespace ProjectOne.Shared
{
	// 인벤토리·보관함 칸 수 규칙 — 클라·서버 공용.
	//
	// 최대 칸 수 = 기본값 + 상점에서 구매한 확장분(InventoryDto 의 *CapacityBonus).
	public static class InventoryRules
	{
		public const int BaseInventoryCapacity = 40;
		public const int BaseStashCapacity = 20;

		public static int GetInventoryCapacity(int bonus)
		{
			return BaseInventoryCapacity + bonus;
		}

		public static int GetStashCapacity(int bonus)
		{
			return BaseStashCapacity + bonus;
		}
	}
}
