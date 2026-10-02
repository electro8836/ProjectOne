using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 마스터리 — 스킬트리 저장, 지식의 서 사용. 경험치 적립은 경험치를 주는 펑션(FieldSettle·DungeonClear)이 직접 한다.
	public class MasteryFunctions
	{
		// SaveMasteryTree — 바뀐 트리의 최종 상태를 서버 경험치·포인트 기준으로 검증하고 노드만 교체한다.
		// 하나라도 규칙을 어기면 아무것도 저장하지 않는다(부분 저장은 트리 간 일관성을 깨지 않지만 원인 추적이 어려워진다).
		public Stream SaveMasteryTree()
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

				SaveMasteryTreeRequest req = JsonConvert.DeserializeObject<SaveMasteryTreeRequest>(Backend.Content["req"].ToString());
				if (req == null || req.trees == null || req.trees.Length == 0)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_MASTERY", out MasteryDto mastery, out string masteryErr) == false)
				{
					return FuncResult.Error(masteryErr);
				}

				for (int i = 0; i < req.trees.Length; i++)
				{
					MasteryProgressDto tree = req.trees[i];
					if (tree == null)
					{
						return FuncResult.Error("null tree");
					}

					WeaponMastery id = (WeaponMastery)tree.masteryId;
					MasteryProgressDto server = MasteryRules.FindOrCreate(mastery, id);

					string invalid = MasteryRules.ValidateTree(id, tree.nodeIds, tree.nodeLevels,
						server.totalExp, server.itemPointUsed, mastery.achievementPoint);
					if (invalid != null)
					{
						return FuncResult.Error("invalid tree " + tree.masteryId + ": " + invalid);
					}

					server.nodeIds = new List<int>(tree.nodeIds);
					server.nodeLevels = new List<int>(tree.nodeLevels);
				}

				var updateResult = Backend.GameData.Update("USER_MASTERY", new Where(), toParam(mastery));
				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("USER_MASTERY Update Failed: " + updateResult.GetErrorCode());
				}

				SaveMasteryTreeResponse response = new SaveMasteryTreeResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// UseSkillPointItem — 지식의 서 1개를 차감하고 대상 마스터리의 itemPointUsed 를 올린다(소모품 설계 5.3).
		public Stream UseSkillPointItem()
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

				UseSkillPointItemRequest req = JsonConvert.DeserializeObject<UseSkillPointItemRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 1. 아이템이 지식의 서인가 — 수량은 EffectParam_2(비면 1), 클라 ConsumableCatalog 와 같은 규칙.
				Table_Consumable.Row consumable = Table_Consumable.Get(req.itemId);
				if (consumable == null || consumable.ConsumeEffect != ConsumeEffect.SkillPoint)
				{
					return FuncResult.Error("not a skill point item: " + req.itemId);
				}

				int amount;
				if (int.TryParse(consumable.EffectParam_2, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) == false || amount <= 0)
				{
					amount = 1;
				}

				WeaponMastery id = (WeaponMastery)req.masteryId;
				if (id == WeaponMastery.None || Table_WeaponMastery.Get(id) == null)
				{
					return FuncResult.Error("invalid mastery: " + req.masteryId);
				}

				// 2. 보유·상한
				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (MyData.Load("USER_MASTERY", out MasteryDto mastery, out string masteryErr) == false)
				{
					return FuncResult.Error(masteryErr);
				}

				if (InventoryUtil.TrySpendItem(inventory, req.itemId, 1) == false)
				{
					return FuncResult.Error("not owned: " + req.itemId);
				}

				MasteryProgressDto progress = MasteryRules.FindOrCreate(mastery, id);
				int cap = MasteryRules.GetMaxPoint(SkillPoint.SkillPoint_Item);
				if (cap > 0 && progress.itemPointUsed + amount > cap)
				{
					return FuncResult.Error("item point cap reached");
				}

				progress.itemPointUsed += amount;

				// 3. 원자 저장
				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_MASTERY", new Where(), toParam(mastery)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				UseSkillPointItemResponse response = new UseSkillPointItemResponse();
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
