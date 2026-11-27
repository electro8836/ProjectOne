namespace ProjectOne.Shared
{
	// 제작·재련 패킷. 판정·비용은 서버가 ForgeRules 로 직접 하고, 확률은 서버가 굴린다.

	// 제작 — 재료 조합·기초 제작·티어 올리기 공용.
	// uid 는 티어 올리기에서만 쓴다(이전 장비). count 는 재료 조합에서만 쓴다(묶음 수, 나머지는 1).
	[System.Serializable]
	public class ForgeCraftRequest
	{
		public int recipeId;
		public long uid;
		public int count = 1;
	}

	// crafted 가 false 면 티어 올리기 실패다 — 재료·재화만 차감되고 장비는 실패 횟수만 올라간다.
	// equipment 는 바뀐(티어 올리기) 또는 새로 만든(기초 제작) 장비의 최종 상태, 재료 조합이면 null.
	// 재화·재료는 절대값이 아닌 증감량만 준다 (EquipmentGrowthResponse 와 같은 이유).
	[System.Serializable]
	public class ForgeCraftResponse : ServerResponse
	{
		public bool crafted;
		public EquipmentInstanceDto equipment;
		public OwnedItemDto[] spentItems;
		public CurrencyAmountDto[] spent;
		public OwnedItemDto[] gainedItems;
	}

	// 재련 — 품질을 올린다. 서버 품질이 fromQuality 와 다르면 바꾸지 않고 현재 상태를 돌려준다.
	[System.Serializable]
	public class ForgeReforgeRequest
	{
		public long uid;
		public int fromQuality;
	}

	[System.Serializable]
	public class ForgeReforgeResponse : ServerResponse
	{
		public EquipmentInstanceDto equipment;
		public CurrencyAmountDto[] spent;
		public int rise;	// 이번에 오른 품질(0.1% 단위). 상태만 맞춘 응답이면 0
	}
}
