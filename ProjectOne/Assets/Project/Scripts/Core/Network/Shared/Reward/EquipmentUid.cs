namespace ProjectOne.Shared
{
	// 장비 인스턴스 UID 대역.
	//
	//   [0, 2^32)        — 서버 채번(InventoryDto.nextEquipmentUid). 던전 클리어·상점·최초 지급
	//   [2^32, ...)      — 필드 드랍. (epoch << 32) | (killIndex << 8) | rewardIndex
	//
	// 필드 드랍은 배치 정산이라 클라가 줍는 시점에 UID 가 필요하다. 줍는 순서와 무관하게
	// 클라와 서버가 **각자 같은 값을 계산**하도록 처치 좌표로 UID 를 만든다.
	public static class EquipmentUid
	{
		public const long FieldBase = 1L << 32;

		// 처치 1건에서 나올 수 있는 보상 수 상한(rewardIndex 8비트).
		public const int MaxRewardsPerKill = 256;

		public static long ForFieldDrop(int epoch, int killIndex, int rewardIndex)
		{
			return ((long)epoch << 32) | ((long)killIndex << 8) | (long)rewardIndex;
		}

		public static bool IsFieldDrop(long uid)
		{
			return uid >= FieldBase;
		}
	}
}
