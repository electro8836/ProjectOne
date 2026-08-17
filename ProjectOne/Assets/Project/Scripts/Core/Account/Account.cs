using ProjectOne.Mastery;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.UserData
{
	// 유저 계정 — 도메인 모델(Inventory/Loadout/MasteryBook 등)을 소유하는 단일 진입점.
	// 스스로 로드하지 않고 DataLoadState 가 도메인별 Set 으로 DTO 를 주입한다(서버 수신 대비).
	public sealed class Account : Singleton<Account>
	{
		public Inventory Inventory { get; private set; }
		public Loadout Loadout { get; private set; }
		public MasteryBook Mastery { get; private set; }
		public ClearedDungeons ClearedDungeons { get; private set; }
		public Wallet Wallet { get; private set; }

		private Account()
		{
			Inventory = new Inventory(null);
			Loadout = new Loadout(null);
			Mastery = new MasteryBook(null);
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

		public void SetMastery(MasteryDto data)
		{
			Mastery = new MasteryBook(data);
		}

		public void SetClearedDungeons(ClearedDungeonsDto data)
		{
			ClearedDungeons = new ClearedDungeons(data);
		}

		public void SetCurrency(CurrencyDto data)
		{
			Wallet = new Wallet(data);
		}

		// 경험치 적립 단일 진입점 (마스터리 설계 5.2).
		//
		// 모든 경험치는 캐릭터와 **현재 장착 무기의 마스터리**에 같은 값이 들어간다. 출처를 가리지 않는다.
		// 호출부가 Loadout.AddExp 를 직접 부르면 마스터리 적립이 빠지므로 여기를 유일한 입구로 둔다.
		//
		// Stat_ExpBonus 는 적 처치분에만 곱하며 그 곱셈은 보상 지급기가 소유한다 — 여기는 이미 곱해진 값을 받는다.
		public void AddExp(int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			Loadout.AddExp(amount);
			Mastery.AddExpToCurrent(amount);
		}

		// 서버가 절대값으로 내려준 캐릭터 경험치를 반영한다(던전 클리어 등).
		// 마스터리는 절대값을 모르므로 증가분만 적립한다 — 서버 이관 시 마스터리도 권위값으로 바뀐다(STEP 14).
		public void SetExpAuthoritative(int newExp)
		{
			int gained = newExp - Loadout.Exp;
			Loadout.SetExp(newExp);

			if (gained > 0)
			{
				Mastery.AddExpToCurrent(gained);
			}
		}
	}
}
