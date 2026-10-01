using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	public class Loadout
	{
		// SaveLoadout — 8슬롯 장착 저장. 슬롯 값은 장비 인스턴스 UID 다(0 = 미장착).
		// 보유 검증 후 USER_LOADOUT 슬롯과 USER_INVENTORY 장비의 equippedSlot 을 함께 갱신한다(서버 권위).
		// 클라는 클릭마다 보내지 않고 화면 닫기·앱 종료 시 1회만 호출한다(패킷 절약).
		public Stream SaveLoadout()
		{
			try
			{
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				string reqJson = Backend.Content["req"].ToString();
				SaveLoadoutRequest req = JsonConvert.DeserializeObject<SaveLoadoutRequest>(reqJson);
				if (req == null || req.slots == null || req.slots.Length != LoadoutDto.SlotCount)
				{
					return FuncResult.Error("req parse failed");
				}

				// 1. 인벤토리 로드 → 장착하려는 UID 보유·중복 검증(클라 값 불신).
				//    0번 자리는 EquipSlotTypes.None 이라 비어 있어야 한다.
				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (req.slots[0] != 0)
				{
					return FuncResult.Error("slot 0 must be empty");
				}

				HashSet<long> requested = new HashSet<long>();
				for (int i = 1; i < req.slots.Length; i++)
				{
					long uid = req.slots[i];
					if (uid == 0)
					{
						continue;
					}

					if (findEquipment(inventory, uid) == null)
					{
						return FuncResult.Error("not owned equipment: " + uid);
					}

					if (requested.Add(uid) == false)
					{
						return FuncResult.Error("duplicate equipment: " + uid);
					}
				}

				// 2. 로드아웃 로드 → 슬롯만 교체(레벨·경험치 보존).
				if (MyData.Load("USER_LOADOUT", out LoadoutDto loadout, out string loadoutErr) == false)
				{
					return FuncResult.Error(loadoutErr);
				}

				loadout.slots = req.slots;

				// 3. 장비 인스턴스의 equippedSlot 을 슬롯과 일치시킨다 — 둘 중 하나만 바뀌면 다음 로드에서 어긋난다.
				for (int i = 0; i < inventory.equipments.Count; i++)
				{
					inventory.equipments[i].equippedSlot = (int)EDT.EquipSlotTypes.None;
				}

				for (int i = 1; i < req.slots.Length; i++)
				{
					if (req.slots[i] != 0)
					{
						findEquipment(inventory, req.slots[i]).equippedSlot = i;
					}
				}

				// 4. 트랜잭션 원자 쓰기. 펑션 컨텍스트는 owner 인자가 비어 UndefinedParameterException 나므로 new Where() 사용.
				Param loadoutParam = new Param();
				loadoutParam.Add("Data", JsonConvert.SerializeObject(loadout));
				Param inventoryParam = new Param();
				inventoryParam.Add("Data", JsonConvert.SerializeObject(inventory));

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_LOADOUT", new Where(), loadoutParam));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), inventoryParam));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				SaveLoadoutResponse response = new SaveLoadoutResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		private static EquipmentInstanceDto findEquipment(InventoryDto inventory, long uid)
		{
			for (int i = 0; i < inventory.equipments.Count; i++)
			{
				EquipmentInstanceDto equipment = inventory.equipments[i];
				if (equipment != null && equipment.uid == uid)
				{
					return equipment;
				}
			}

			return null;
		}
	}
}
