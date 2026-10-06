using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	public class StashFunctions
	{
		// SaveStash — 보관함에 넣어 둔 장비 UID 전체(최종 상태)를 저장한다.
		// 보유·중복·칸 수 검증 후 USER_INVENTORY 장비의 inStash 를 통째로 다시 맞춘다(서버 권위).
		// 클라는 이동마다 보내지 않고 화면 닫기·앱 종료 시 1회만 호출한다(패킷 절약).
		public Stream SaveStash()
		{
			try
			{
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				string reqJson = Backend.Content["req"].ToString();
				SaveStashRequest req = JsonConvert.DeserializeObject<SaveStashRequest>(reqJson);
				if (req == null || req.stashUids == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (req.stashUids.Length > InventoryRules.GetStashCapacity(inventory.stashCapacityBonus))
				{
					return FuncResult.Error("stash is full: " + req.stashUids.Length);
				}

				// 1. 보관하려는 UID 보유·중복 검증(클라 값 불신). 장착 여부는 따지지 않는다 — 장착한 채로 보관할 수 있다.
				HashSet<long> requested = new HashSet<long>();
				for (int i = 0; i < req.stashUids.Length; i++)
				{
					long uid = req.stashUids[i];
					EquipmentInstanceDto equipment = findEquipment(inventory, uid);
					if (equipment == null)
					{
						return FuncResult.Error("not owned equipment: " + uid);
					}

					if (requested.Add(uid) == false)
					{
						return FuncResult.Error("duplicate equipment: " + uid);
					}
				}

				// 2. 목록에 있는 것만 보관함, 나머지는 인벤토리.
				for (int i = 0; i < inventory.equipments.Count; i++)
				{
					EquipmentInstanceDto equipment = inventory.equipments[i];
					if (equipment != null)
					{
						equipment.inStash = requested.Contains(equipment.uid);
					}
				}

				Param inventoryParam = new Param();
				inventoryParam.Add("Data", JsonConvert.SerializeObject(inventory));

				var updateResult = Backend.GameData.Update("USER_INVENTORY", new Where(), inventoryParam);
				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("Update failed: " + updateResult.GetErrorCode());
				}

				SaveStashResponse response = new SaveStashResponse();
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
