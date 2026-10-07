using System.Collections.Generic;
using EDT;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 추첨 결과(RolledReward)를 유저 데이터 DTO 에 반영한다 — 서버 측 지급의 단일 입구.
	// 던전 클리어·상점 상자·퀘스트·우편 등 보상을 주는 펑션은 모두 이 경로를 쓴다.
	//
	// DTO 를 메모리에서만 고친다. 저장(트랜잭션)은 호출자가 다른 테이블과 묶어서 한다.
	public sealed class RewardApplier
	{
		private readonly InventoryDto _inventory;
		private readonly CurrencyDto _currency;

		// 수집품(펫·코스튬) 지급 대상 — null 이면 그 경로에서는 수집품을 지급하지 않는다(상점·필드보스만 넘긴다).
		private readonly PetDto _pet;
		private readonly CostumeDto _costume;

		private readonly List<GrantedRewardDto> _granted = new List<GrantedRewardDto>();
		private readonly List<EquipmentInstanceDto> _equipments = new List<EquipmentInstanceDto>();

		// 자동 분해 환급 계산용 버퍼
		private readonly List<CurrencyCost> _refundBuffer = new List<CurrencyCost>(1);

		public RewardApplier(InventoryDto inventory, CurrencyDto currency)
			: this(inventory, currency, null, null)
		{
		}

		public RewardApplier(InventoryDto inventory, CurrencyDto currency, PetDto pet, CostumeDto costume)
		{
			_inventory = inventory;
			_currency = currency;
			_pet = pet;
			_costume = costume;
		}

		// 수집품이 새로 들어가 USER_PET / USER_COSTUME 을 저장해야 하는지.
		public bool PetChanged { get; private set; }
		public bool CostumeChanged { get; private set; }

		// 지급된 스택 아이템·재화 — 응답 rewards 로 내려간다.
		public GrantedRewardDto[] Granted
		{
			get { return _granted.ToArray(); }
		}

		// 지급된 장비 인스턴스(UID 채번 완료) — 응답 equipments 로 내려간다.
		public EquipmentInstanceDto[] Equipments
		{
			get { return _equipments.ToArray(); }
		}

		public void ApplyAll(List<RolledReward> rolled)
		{
			for (int i = 0; i < rolled.Count; i++)
			{
				Apply(rolled[i], 0);
			}
		}

		// 결과 1건 반영. equipmentUid 가 0 이면 nextEquipmentUid 로 채번하고,
		// 0 이 아니면 그 값을 쓴다(필드 드랍 — 클라와 서버가 처치 좌표로 같은 UID 를 계산한다).
		public void Apply(RolledReward rolled, long equipmentUid)
		{
			if (rolled.count <= 0)
			{
				return;
			}

			if (rolled.type == RewardType.Currency)
			{
				addCurrency((int)rolled.currency, rolled.count);
				_granted.Add(makeGranted(rolled.type, (int)rolled.currency, rolled.count));
				return;
			}

			// 장비는 인스턴스 단위다 — UID 는 지정값(필드 드랍)이 없으면 서버가 nextEquipmentUid 로 채번한다.
			if (rolled.isEquipment == true)
			{
				EquipmentInstanceDto instance = new EquipmentInstanceDto();
				instance.uid = (equipmentUid != 0) ? equipmentUid : _inventory.nextEquipmentUid;
				instance.itemId = rolled.itemId;
				instance.grade = (int)rolled.grade;
				instance.level = 1;
				instance.quality = rolled.quality;
				instance.equippedSlot = (int)EquipSlotTypes.None;

				if (equipmentUid == 0)
				{
					_inventory.nextEquipmentUid++;
				}

				// 응답에는 그대로 싣는다 — 클라가 같은 조건으로 판정해 자동 분해분을 재화로 반영한다(RewardGranter).
				_equipments.Add(instance);

				// 자동 분해 — 인벤토리에 넣지 않고 환급 재화만 준다.
				if (EquipmentDecomposeRules.IsAutoTarget(_inventory.decomposeSetting, rolled.grade, rolled.quality) == true)
				{
					EquipmentGrowthRules.GetDecomposeRefund(instance.itemId, rolled.grade, instance.level, _refundBuffer);
					for (int i = 0; i < _refundBuffer.Count; i++)
					{
						addCurrency((int)_refundBuffer[i].currency, _refundBuffer[i].amount);
					}

					return;
				}

				_inventory.equipments.Add(instance);
				return;
			}

			// 수집품은 인벤토리가 아니라 보유로 간다. 이미 가졌으면 지급도 표시도 하지 않는다(중복 보유가 없다).
			if (CollectionRules.IsCollection(rolled.itemId) == true)
			{
				if (tryAddCollection(rolled.itemId) == true)
				{
					_granted.Add(makeGranted(rolled.type, rolled.itemId, 1));
				}

				return;
			}

			addItem(rolled.itemId, rolled.count);
			_granted.Add(makeGranted(rolled.type, rolled.itemId, rolled.count));
		}

		// 이미 보유 중인 수집품인지 — 상점이 구매 전에 막는 데 쓴다.
		public static bool IsCollectionOwned(PetDto pet, CostumeDto costume, int itemId)
		{
			if (CollectionRules.IsPet(itemId) == true)
			{
				return pet != null && findPet(pet, itemId) != null;
			}

			if (CollectionRules.IsCostume(itemId) == true)
			{
				return costume != null && costume.owned.Contains(itemId) == true;
			}

			return false;
		}

		private bool tryAddCollection(int itemId)
		{
			if (CollectionRules.IsPet(itemId) == true)
			{
				Table_Pet.Row row = Table_Pet.Get(itemId);
				if (_pet == null || row == null || findPet(_pet, itemId) != null)
				{
					return false;
				}

				PetEntryDto entry = new PetEntryDto();
				entry.petId = itemId;
				entry.level = 1;
				entry.grade = (int)row.Grade;
				_pet.pets.Add(entry);
				PetChanged = true;
				return true;
			}

			if (CollectionRules.IsCostume(itemId) == true)
			{
				if (_costume == null || Table_Costume.Get(itemId) == null || _costume.owned.Contains(itemId) == true)
				{
					return false;
				}

				_costume.owned.Add(itemId);
				CostumeChanged = true;
				return true;
			}

			return false;
		}

		private static PetEntryDto findPet(PetDto pet, int petId)
		{
			for (int i = 0; i < pet.pets.Count; i++)
			{
				if (pet.pets[i] != null && pet.pets[i].petId == petId)
				{
					return pet.pets[i];
				}
			}

			return null;
		}

		private void addItem(int itemId, int count)
		{
			for (int i = 0; i < _inventory.items.Count; i++)
			{
				if (_inventory.items[i].itemId == itemId)
				{
					_inventory.items[i].count += count;
					return;
				}
			}

			OwnedItemDto item = new OwnedItemDto();
			item.itemId = itemId;
			item.count = count;
			_inventory.items.Add(item);
		}

		private void addCurrency(int currencyId, int amount)
		{
			for (int i = 0; i < _currency.amounts.Count; i++)
			{
				if (_currency.amounts[i].currencyId == currencyId)
				{
					_currency.amounts[i].amount += amount;
					return;
				}
			}

			CurrencyAmountDto entry = new CurrencyAmountDto();
			entry.currencyId = currencyId;
			entry.amount = amount;
			_currency.amounts.Add(entry);
		}

		private static GrantedRewardDto makeGranted(RewardType type, int itemId, int count)
		{
			GrantedRewardDto dto = new GrantedRewardDto();
			dto.rewardType = (int)type;
			dto.itemId = itemId;
			dto.count = count;
			return dto;
		}
	}
}
