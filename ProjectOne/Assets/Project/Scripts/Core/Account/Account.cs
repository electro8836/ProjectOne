using ProjectOne.ServerData;
using ProjectOne.Utils;

namespace ProjectOne.UserData
{
	// 유저 계정 — 도메인 모델(Inventory/Loadout/SkillBook)을 소유하는 단일 진입점.
	// 스스로 로드하지 않고 DataLoadState 가 도메인별 Set 으로 데이터를 주입한다(서버 수신 대비).
	public sealed class Account : Singleton<Account>
	{
		public Inventory Inventory { get; private set; }
		public Loadout Loadout { get; private set; }
		public SkillBook SkillBook { get; private set; }
		public Wallet Wallet { get; private set; }

		private Account()
		{
			// 셋팅 전 기본 빈 데이터 (Set 전 접근해도 null 안전)
			Inventory = new Inventory(null);
			Loadout = new Loadout(null);
			SkillBook = new SkillBook(null);
			Wallet = new Wallet(null);
		}

		// 도메인별 개별 셋팅 — 추후 도메인 추가 시 Set 메서드만 늘리면 됨
		public void SetInventory(InventoryData data)
		{
			Inventory = new Inventory(data);
		}

		public void SetCharacter(CharacterData data)
		{
			Loadout = new Loadout(data);
		}

		public void SetSkill(SkillData data)
		{
			SkillBook = new SkillBook(data);
		}

		public void SetCurrency(CurrencyData data)
		{
			Wallet = new Wallet(data);
		}

		// 서버 응답의 갱신 도메인 데이터 일괄 반영 — non-null 인 도메인만 교체한다.
		// 로컬 구현은 sync 가 null 이라 no-op(이미 Account 를 직접 변경했음).
		public void ApplySync(AccountSyncData sync)
		{
			if (sync == null)
			{
				return;
			}

			if (sync.character != null)
			{
				SetCharacter(sync.character);
			}

			if (sync.inventory != null)
			{
				SetInventory(sync.inventory);
			}

			if (sync.skill != null)
			{
				SetSkill(sync.skill);
			}

			if (sync.currency != null)
			{
				SetCurrency(sync.currency);
			}
		}
	}
}
