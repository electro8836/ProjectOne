using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 마스터리 — 스킬 슬롯 저장. 경험치 적립은 경험치를 주는 펑션(FieldSettle·DungeonClear·FieldBossKill)이 직접 한다.
	public class MasteryFunctions
	{
		// SaveMasterySlots — 바뀐 슬롯의 최종 상태를 서버 경험치 기준으로 검증하고 슬롯만 교체한다(마스터리 설계 7.4).
		// 하나라도 규칙을 어기면 아무것도 저장하지 않는다 — 클라가 dirty 를 되살려 다음에 재전송한다.
		public Stream SaveMasterySlots()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				SaveMasterySlotsRequest req = JsonConvert.DeserializeObject<SaveMasterySlotsRequest>(Backend.Content["req"].ToString());
				if (req == null || req.masteries == null || req.masteries.Length == 0)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_MASTERY", out MasteryDto mastery, out string masteryErr) == false)
				{
					return FuncResult.Error(masteryErr);
				}

				for (int i = 0; i < req.masteries.Length; i++)
				{
					MasteryProgressDto sent = req.masteries[i];
					if (sent == null)
					{
						return FuncResult.Error("null mastery");
					}

					WeaponMastery id = (WeaponMastery)sent.masteryId;
					MasteryProgressDto server = MasteryRules.FindOrCreate(mastery, id);

					string invalid = MasteryRules.ValidateSlots(id, sent.slots, server.totalExp);
					if (invalid != null)
					{
						return FuncResult.Error("invalid slots " + sent.masteryId + ": " + invalid);
					}

					server.slots = new List<int>(sent.slots);
				}

				var updateResult = Backend.GameData.Update("USER_MASTERY", new Where(), toParam(mastery));
				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("USER_MASTERY Update Failed: " + updateResult.GetErrorCode());
				}

				// 슬롯은 전투력·대표 레벨에 들어가지 않는다 — 랭킹 갱신이 필요 없다.
				SaveMasterySlotsResponse response = new SaveMasterySlotsResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 경험치 적립 대상 검증 — 그 마스터리의 무기를 실제로 갖고 있어야 한다(0 = 미착용, 적립 없음).
		// 장착 여부까지는 보지 않는다: 장착은 묶음 저장이라 서버값이 늦을 수 있다. 같은 경험치를 어느 축에 넣느냐의 문제라 이득이 없다.
		public static bool OwnsWeaponFor(InventoryDto inventory, int masteryId)
		{
			if (masteryId == 0)
			{
				return true;
			}

			Table_WeaponMastery.Row mastery = Table_WeaponMastery.Get((WeaponMastery)masteryId);
			if (mastery == null || mastery.WeaponType == WeaponType.None)
			{
				return false;
			}

			for (int i = 0; i < inventory.equipments.Count; i++)
			{
				EquipmentInstanceDto owned = inventory.equipments[i];
				Table_Equipment.Row equipment = (owned != null) ? Table_Equipment.Get(owned.itemId) : null;
				if (equipment != null && equipment.WeaponType == mastery.WeaponType)
				{
					return true;
				}
			}

			return false;
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
