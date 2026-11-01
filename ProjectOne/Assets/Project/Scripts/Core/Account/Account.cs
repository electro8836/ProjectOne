using ProjectOne.DailyBonuses;
using ProjectOne.Field;
using ProjectOne.HeroPasses;
using ProjectOne.Mastery;
using ProjectOne.Pets;
using ProjectOne.Quests;
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
		public QuestBook Quests { get; private set; }
		public CostumeBook Costume { get; private set; }
		public PetBook Pet { get; private set; }
		public Wallet Wallet { get; private set; }
		public DailyBonusBook DailyBonus { get; private set; }
		public HeroPassBook HeroPass { get; private set; }

		private Account()
		{
			Inventory = new Inventory(null);
			Loadout = new Loadout(null);
			Mastery = new MasteryBook(null);
			Quests = new QuestBook(null);
			Costume = new CostumeBook(null);
			Pet = new PetBook(null);
			Wallet = new Wallet(null);
			DailyBonus = new DailyBonusBook(null);

			// 로그인 전 기본값 — 시즌 기준일은 SetHeroPass 로 서버 값(계정 생성일)을 받는다.
			HeroPass = new HeroPassBook(null);
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

		public void SetQuests(QuestDto data)
		{
			Quests = new QuestBook(data);
		}

		public void SetCostume(CostumeDto data)
		{
			Costume = new CostumeBook(data);
		}

		public void SetPet(PetDto data)
		{
			Pet = new PetBook(data);
		}

		public void SetCurrency(CurrencyDto data)
		{
			Wallet = new Wallet(data);
		}

		public void SetDailyBonus(DailyBonusDto data)
		{
			DailyBonus = new DailyBonusBook(data);
		}

		public void SetHeroPass(HeroPassDto data)
		{
			HeroPass = new HeroPassBook(data);
		}

		// 경험치 적립 단일 진입점 (마스터리 설계 5.2).
		//
		// 모든 경험치는 **현재 장착 무기의 마스터리**에 들어간다. 출처를 가리지 않는다. 캐릭터 레벨은 없다.
		// 무기를 들지 않았으면 버려진다.
		//
		// Stat_ExpBonus 는 적 처치분에만 곱하며 그 곱셈은 보상 지급기가 소유한다 — 여기는 이미 곱해진 값을 받는다.
		public void AddExp(int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			// 마스터리 레벨이 오르면 레벨 보너스 스탯과 슬롯(자동 장착)이 달라진다 — 즉시 다시 굽지 않으면
			// 다음 장비 교체 때까지 옛 스탯으로 싸운다.
			if (Mastery.AddExpToCurrent(amount) == true)
			{
				Loadout.ReapplyMastery();
			}
		}

		// 서버가 정산 후 내려준 마스터리 상태로 경험치를 덮는다(던전 클리어·필드보스).
		// 아직 서버에 올라가지 않은 필드 처치 경험치를 마스터리별로 더한다 — 빼면 그만큼 경험치가 줄어든다.
		public void SetMasteryAuthoritative(MasteryDto server)
		{
			if (Mastery.ApplyAuthoritative(server, FieldKillLedger.Instance.UnsettledExpByMastery()) == true)
			{
				Loadout.ReapplyMastery();
			}
		}
	}
}
