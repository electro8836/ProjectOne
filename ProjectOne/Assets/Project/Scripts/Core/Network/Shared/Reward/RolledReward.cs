using EDT;

namespace ProjectOne.Shared
{
	// 추첨 결과 하나 — 아직 인벤/지갑에 반영되지 않은 값이다. 클라·서버 공용.
	// 장비는 개수만큼 1건씩 나오고 등급·품질까지 확정돼 있다(인스턴스 생성·UID 채번은 반영하는 쪽 몫).
	public struct RolledReward
	{
		public RewardType type;
		public int itemId;				// Item / ItemPool
		public EDT.Currency currency;	// Currency
		public int count;

		public bool isEquipment;
		public ItemGradeType grade;
		public int quality;
	}
}
