using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.UserData
{
	// 유저 계정 — 도메인 모델(Inventory/Loadout/SkillBook)을 소유하는 단일 진입점.
	// 스스로 로드하지 않고 DataLoadState 가 도메인별 Set 으로 DTO 를 주입한다(서버 수신 대비).
	public sealed class Account : Singleton<Account>
	{
		public Inventory Inventory { get; private set; }
		public Loadout Loadout { get; private set; }
		public SkillBook SkillBook { get; private set; }
		public ClearedDungeons ClearedDungeons { get; private set; }
		public Wallet Wallet { get; private set; }

		private Account()
		{
			Inventory = new Inventory(null);
			Loadout = new Loadout(null);
			SkillBook = new SkillBook(null);
			ClearedDungeons = new ClearedDungeons(null);
			Wallet = new Wallet(null);
		}

		// 도메인별 개별 셋팅 — 공유 DTO 를 받아 도메인 모델로 변환 보유. 추후 도메인 추가 시 Set 메서드만 늘리면 됨
		public void SetInventory(InventoryDto data)
		{
			Inventory = new Inventory(data);
		}

		public void SetLoadout(LoadoutDto data)
		{
			Loadout = new Loadout(data);
		}

		public void SetSkill(SkillDto data)
		{
			SkillBook = new SkillBook(data);
		}

		public void SetClearedDungeons(ClearedDungeonsDto data)
		{
			ClearedDungeons = new ClearedDungeons(data);
		}

		public void SetCurrency(CurrencyDto data)
		{
			Wallet = new Wallet(data);
		}
	}
}
