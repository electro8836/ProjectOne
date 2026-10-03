using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 상품 1개의 구매 기록(직렬화 DTO).
	[System.Serializable]
	public class ShopPurchaseDto
	{
		public int goodsId;
		public int count;

		// 마지막으로 산 날 (ResetDay 기준). 일일 리셋 상품은 오늘 값과 다르면 0회로 본다.
		public int resetDay;
	}

	// 상점 구매 횟수 직렬화 DTO — 서버-클라 공유 영속 스키마. 판정은 ShopRules 가 한다.
	// JsonUtility 가 Dictionary 직렬화 불가 → List 보관 (CurrencyDto 와 같은 관례).
	[System.Serializable]
	public class ShopDto
	{
		public List<ShopPurchaseDto> purchases = new List<ShopPurchaseDto>();
	}
}
