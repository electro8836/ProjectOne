using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Shared;

namespace ProjectOne.UserData
{
	// 인벤토리 모델(인메모리) — 두 종류를 함께 보유한다.
	//
	//   스택 아이템   재료·소모품·수집품. itemId → count 로 합쳐진다.
	//   장비 인스턴스 UID 단위. 같은 아이템이라도 등급·강화·순도·품질이 달라 합칠 수 없다 (아이템 설계 4장).
	//
	// 공유 DTO(InventoryDto)를 받아 런타임 모델로 변환하고, 저장/전송 시 ToDto() 로 역변환한다.
	// 영속은 서버(Backnd 함수)가 담당, 변경 시 알림만 발행. UID 채번은 서버 이관 전까지 클라가 한다(STEP 14).
	public sealed class Inventory
	{
		// 순서 보존용 목록 + 빠른 조회용 인덱스(동일 인스턴스 참조 공유)
		private readonly List<OwnedItem> _items = new List<OwnedItem>();
		private readonly Dictionary<int, OwnedItem> _itemIndex = new Dictionary<int, OwnedItem>();

		private readonly List<EquipmentInstance> _equipments = new List<EquipmentInstance>();
		private readonly Dictionary<long, EquipmentInstance> _equipIndex = new Dictionary<long, EquipmentInstance>();

		private long _nextUid = 1;

		public Inventory(InventoryDto dto)
		{
			buildFromDto(dto);
		}

		// ── 스택 아이템 ───────────────────────────────────────────────

		// 보유 여부 — count >= 1 이면 사용 가능
		public bool Has(int itemId)
		{
			return GetCount(itemId) >= 1;
		}

		public int GetCount(int itemId)
		{
			OwnedItem item;
			if (_itemIndex.TryGetValue(itemId, out item) == true)
			{
				return item.count;
			}

			return 0;
		}

		public IReadOnlyList<OwnedItem> GetAllItems()
		{
			return _items;
		}

		// 아이템 획득 — 없으면 생성, 있으면 count 증가
		public void Add(int itemId, int amount = 1)
		{
			if (itemId <= 0 || amount <= 0)
			{
				return;
			}

			OwnedItem item;
			if (_itemIndex.TryGetValue(itemId, out item) == false)
			{
				item = new OwnedItem();
				item.itemId = itemId;
				_items.Add(item);
				_itemIndex.Add(itemId, item);
			}

			item.count += amount;
			EventManager.Instance.Publish(new InventoryChangeEvent(item.itemId, item.count));
		}

		// 아이템 소모 — 보유 수량이 충분하면 차감 후 true
		public bool TrySpend(int itemId, int amount = 1)
		{
			if (itemId <= 0 || amount <= 0)
			{
				return false;
			}

			OwnedItem item;
			if (_itemIndex.TryGetValue(itemId, out item) == false || item.count < amount)
			{
				return false;
			}

			item.count -= amount;
			EventManager.Instance.Publish(new InventoryChangeEvent(item.itemId, item.count));
			return true;
		}

		// ── 장비 인스턴스 ─────────────────────────────────────────────

		public EquipmentInstance GetEquipment(long uid)
		{
			EquipmentInstance instance;
			_equipIndex.TryGetValue(uid, out instance);
			return instance;
		}

		public IReadOnlyList<EquipmentInstance> GetAllEquipments()
		{
			return _equipments;
		}

		// 생성된 인스턴스를 인벤토리에 넣는다. UID 가 비어 있으면 여기서 채번한다.
		public EquipmentInstance AddEquipment(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return null;
			}

			if (instance.uid <= 0)
			{
				instance.uid = _nextUid;
				_nextUid++;
			}
			else if (instance.uid >= _nextUid)
			{
				_nextUid = instance.uid + 1;
			}

			if (_equipIndex.ContainsKey(instance.uid) == true)
			{
				Debug.LogError($"[Inventory] 장비 UID 중복: {instance.uid}");
				return _equipIndex[instance.uid];
			}

			_equipments.Add(instance);
			_equipIndex.Add(instance.uid, instance);
			EventManager.Instance.Publish(new EquipmentChangeEvent(instance.uid));
			return instance;
		}

		// 장비 소멸 — 착용 중이면 거부한다. 해제는 호출자(Loadout)가 먼저 해야 한다.
		public bool RemoveEquipment(long uid)
		{
			EquipmentInstance instance;
			if (_equipIndex.TryGetValue(uid, out instance) == false)
			{
				return false;
			}

			if (instance.IsEquipped == true)
			{
				Debug.LogWarning($"[Inventory] 착용 중인 장비는 소멸시킬 수 없습니다: {uid}");
				return false;
			}

			_equipments.Remove(instance);
			_equipIndex.Remove(uid);
			EventManager.Instance.Publish(new EquipmentChangeEvent(uid));
			return true;
		}

		// 강화/승급 등으로 인스턴스 내용이 바뀐 뒤 알림만 발행한다.
		public void NotifyEquipmentChanged(long uid)
		{
			EventManager.Instance.Publish(new EquipmentChangeEvent(uid));
		}

		// 특정 착용 슬롯에 넣을 수 있는 장비 목록을 채운다(호출자가 버퍼를 소유).
		public void CollectBySlot(EquipSlotTypes slot, List<EquipmentInstance> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			for (int i = 0; i < _equipments.Count; i++)
			{
				Table_Equipment.Row row = _equipments[i].Equipment;
				if (row != null && row.EquipSlotType == slot)
				{
					buffer.Add(_equipments[i]);
				}
			}
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public InventoryDto ToDto()
		{
			InventoryDto dto = new InventoryDto();
			dto.nextEquipmentUid = _nextUid;

			for (int i = 0; i < _items.Count; i++)
			{
				OwnedItem src = _items[i];
				OwnedItemDto entry = new OwnedItemDto();
				entry.itemId = src.itemId;
				entry.count = src.count;
				dto.items.Add(entry);
			}

			for (int i = 0; i < _equipments.Count; i++)
			{
				EquipmentInstance src = _equipments[i];
				EquipmentInstanceDto entry = new EquipmentInstanceDto();
				entry.uid = src.uid;
				entry.itemId = src.itemId;
				entry.grade = (int)src.grade;
				entry.level = src.level;
				entry.purity = (int)src.purity;
				entry.quality = src.quality;
				entry.equippedSlot = (int)src.equippedSlot;
				dto.equipments.Add(entry);
			}

			return dto;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildFromDto(InventoryDto dto)
		{
			_items.Clear();
			_itemIndex.Clear();
			_equipments.Clear();
			_equipIndex.Clear();
			_nextUid = 1;

			if (dto == null)
			{
				return;
			}

			if (dto.nextEquipmentUid > 0)
			{
				_nextUid = dto.nextEquipmentUid;
			}

			if (dto.items != null)
			{
				for (int i = 0; i < dto.items.Count; i++)
				{
					OwnedItemDto src = dto.items[i];
					if (src == null || src.itemId <= 0)
					{
						continue;
					}

					OwnedItem item = new OwnedItem();
					item.itemId = src.itemId;
					item.count = src.count;
					_items.Add(item);
					_itemIndex[item.itemId] = item;
				}
			}

			if (dto.equipments == null)
			{
				return;
			}

			for (int i = 0; i < dto.equipments.Count; i++)
			{
				EquipmentInstanceDto src = dto.equipments[i];
				if (src == null || src.uid <= 0 || src.itemId <= 0)
				{
					continue;
				}

				EquipmentInstance instance = new EquipmentInstance();
				instance.uid = src.uid;
				instance.itemId = src.itemId;
				instance.grade = (ItemGradeType)src.grade;
				instance.level = src.level > 0 ? src.level : 1;
				instance.purity = (EquipPurity)src.purity;
				instance.quality = src.quality;
				instance.equippedSlot = (EquipSlotTypes)src.equippedSlot;

				_equipments.Add(instance);
				_equipIndex[instance.uid] = instance;

				if (instance.uid >= _nextUid)
				{
					_nextUid = instance.uid + 1;
				}
			}
		}
	}
}
