namespace ProjectOne.Shared
{
	// 상점 패킷 — 모든 상품 구매를 서버가 처리한다(구매 횟수 한도 포함).

	// 상품 1개 구매. 가격 차감과 보상 추첨은 서버가 ShopGoods 행으로 직접 한다.
	[System.Serializable]
	public class ShopBuyRequest
	{
		public int goodsId;
		public int count;			// 한 번에 살 개수 — 상자 묶음 오픈용. 0 이하는 1 로 본다
		public bool decomposeAll;	// 상자에서 나온 장비를 전부 분해해 환급 재화로 받는다
	}

	// 서버가 확정한 획득 목록. 가격(열쇠·재화)은 성공 응답을 받은 클라가 같은 행으로 차감한다.
	[System.Serializable]
	public class ShopBuyResponse : ServerResponse
	{
		public GrantedRewardDto[] rewards;			// 스택 아이템·재화
		public EquipmentInstanceDto[] equipments;	// 장비 — 서버가 UID·등급·품질을 확정한 인스턴스
	}
}
