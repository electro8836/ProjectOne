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

		private readonly List<GrantedRewardDto> _granted = new List<GrantedRewardDto>();
		private readonly List<EquipmentInstanceDto> _equipments = new List<EquipmentInstanceDto>();

		public RewardApplier(InventoryDto inventory, CurrencyDto currency)
		{
			_inventory = inventory;
			_currency = currency;
		}

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

				_inventory.equipments.Add(instance);
				_equipments.Add(instance);
				return;
			}

			addItem(rolled.itemId, rolled.count);
			_granted.Add(makeGranted(rolled.type, rolled.itemId, rolled.count));
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
