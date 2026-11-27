using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 제작(재료 조합·기초 제작·티어 올리기)·재련. 판정·비용은 클라와 같은 공유 규칙(ForgeRules)으로 하고,
	// 확률(성공·상승량)은 여기서만 굴린다.
	//
	// 재료(INVENTORY.items)·장비(INVENTORY.equipments)·재화(CURRENCY)를 트랜잭션 하나로 저장한다.
	public class ForgeFunctions
	{
		public Stream ForgeCraft()
		{
			try
			{
				if (begin(out ForgeCraftRequest req, out InventoryDto inventory, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				Table_ForgeRecipe.Row recipe = Table_ForgeRecipe.Get(req.recipeId);
				ForgeRecipeKind kind = ForgeRules.GetKind(recipe);
				if (kind == ForgeRecipeKind.Invalid)
				{
					return FuncResult.Error("invalid recipe: " + req.recipeId);
				}

				int count = 1;
				if (kind == ForgeRecipeKind.Material)
				{
					count = req.count;
					if (count < 1 || count > ForgeRules.MaxMaterialCount)
					{
						return FuncResult.Error("invalid count: " + req.count);
					}
				}

				EquipmentInstanceDto target = null;
				if (kind == ForgeRecipeKind.Upgrade)
				{
					target = findEquipment(inventory, req.uid);
					if (target == null)
					{
						return FuncResult.Error("equipment not owned: " + req.uid);
					}

					if (target.itemId != recipe.SourceItemID || ForgeRules.IsValidUpgrade(recipe) == false)
					{
						return FuncResult.Error("source mismatch: " + target.itemId + " -> " + recipe.ID);
					}
				}
				else if (kind == ForgeRecipeKind.Base && getFreeInventorySlots(inventory) <= 0)
				{
					return FuncResult.Error("inventory full");
				}

				// 재료 → 재화 순으로 검사를 끝낸 뒤 차감한다 — 부분 차감이 남지 않게.
				List<ItemCost> itemCosts = new List<ItemCost>(4);
				ForgeRules.GetItemCosts(recipe, count, itemCosts);
				if (hasItems(inventory, itemCosts) == false)
				{
					return FuncResult.Error("not enough items");
				}

				List<CurrencyCost> costs = new List<CurrencyCost>(2);
				ForgeRules.GetCurrencyCosts(recipe, count, costs);
				if (CurrencyUtil.TrySpendAll(currency, costs) == false)
				{
					return FuncResult.Error("not enough currency");
				}

				for (int i = 0; i < itemCosts.Count; i++)
				{
					InventoryUtil.TrySpendItem(inventory, itemCosts[i].itemId, itemCosts[i].count);
				}

				ForgeCraftResponse response = new ForgeCraftResponse();
				response.success = true;
				response.crafted = true;
				response.spentItems = toItemDto(itemCosts);
				response.spent = CurrencyUtil.ToSpentDto(costs);

				ServerRandomSource rng = new ServerRandomSource();
				switch (kind)
				{
					case ForgeRecipeKind.Material:
						int gained = recipe.ResultCount * count;
						addItem(inventory, recipe.ResultItemID, gained);
						response.gainedItems = new OwnedItemDto[] { makeItem(recipe.ResultItemID, gained) };
						break;

					case ForgeRecipeKind.Base:
						response.equipment = createBase(inventory, recipe, rng);
						break;

					case ForgeRecipeKind.Upgrade:
						int rate = ForgeRules.GetSuccessRate(recipe, target.forgeFails);
						if (ForgeRules.RollSuccess(rate, rng) == true)
						{
							// 같은 UID — 등급·강화·품질은 그대로, 장비 행만 다음 티어로 바뀐다.
							target.itemId = recipe.ResultItemID;
							target.forgeFails = 0;
							target.locked = true;
						}
						else
						{
							target.forgeFails++;
							response.crafted = false;
						}

						response.equipment = target;
						break;
				}

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				// 착용 중인 장비의 티어가 오르면 전투력이 바뀐다.
				if (kind == ForgeRecipeKind.Upgrade && response.crafted == true)
				{
					RankOps.Refresh(null, inventory, null, null, null);
				}

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		public Stream ForgeReforge()
		{
			try
			{
				if (begin(out ForgeReforgeRequest req, out InventoryDto inventory, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				EquipmentInstanceDto target = findEquipment(inventory, req.uid);
				if (target == null)
				{
					return FuncResult.Error("equipment not owned: " + req.uid);
				}

				ForgeReforgeResponse response = new ForgeReforgeResponse();
				response.success = true;
				response.equipment = target;

				// 품질이 어긋났으면(앞 요청 반영 전 등) 바꾸지 않고 현재 상태로 맞추게 한다.
				if (target.quality != req.fromQuality)
				{
					response.spent = new CurrencyAmountDto[0];
					return FuncResult.Json(response);
				}

				ItemGradeType grade = (ItemGradeType)target.grade;
				ReforgeBlock block = ForgeRules.GetReforgeBlock(target.itemId, grade, target.quality);
				if (block != ReforgeBlock.None)
				{
					return FuncResult.Error("cannot reforge: " + block);
				}

				List<CurrencyCost> costs = new List<CurrencyCost>(2);
				ForgeRules.GetReforgeCosts(grade, costs);
				if (costs.Count == 0 || CurrencyUtil.TrySpendAll(currency, costs) == false)
				{
					return FuncResult.Error("not enough currency");
				}

				int next = ForgeRules.RollReforge(grade, target.quality, new ServerRandomSource());
				response.rise = next - target.quality;
				response.spent = CurrencyUtil.ToSpentDto(costs);
				target.quality = next;

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				RankOps.Refresh(null, inventory, null, null, null);

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 1티어 기초 장비 — 등급은 조합법이 정하고, 품질은 드랍과 같은 분포로 굴린다. 결과는 자동 잠금.
		private static EquipmentInstanceDto createBase(InventoryDto inventory, Table_ForgeRecipe.Row recipe, IRandomSource rng)
		{
			int quality;
			EquipmentRoll.TryRollQuality(rng, out quality);

			EquipmentInstanceDto instance = new EquipmentInstanceDto();
			instance.uid = inventory.nextEquipmentUid;
			instance.itemId = recipe.ResultItemID;
			instance.grade = (recipe.ResultGrade != ItemGradeType.None) ? (int)recipe.ResultGrade : (int)ItemGradeType.Normal;
			instance.level = 1;
			instance.quality = quality;
			instance.equippedSlot = (int)EquipSlotTypes.None;
			instance.locked = true;

			inventory.nextEquipmentUid++;
			inventory.equipments.Add(instance);
			return instance;
		}

		// 테이블 로드 → 요청 파싱 → INVENTORY·CURRENCY 로드.
		private static bool begin<T>(out T req, out InventoryDto inventory, out CurrencyDto currency, out string err) where T : class
		{
			req = null;
			inventory = null;
			currency = null;

			if (GameData.EnsureLoaded(out err) == false)
			{
				return false;
			}

			if (Backend.HasKey("req") == false)
			{
				err = "req key is not exist";
				return false;
			}

			req = JsonConvert.DeserializeObject<T>(Backend.Content["req"].ToString());
			if (req == null)
			{
				err = "req parse failed";
				return false;
			}

			if (MyData.Load("USER_INVENTORY", out inventory, out err) == false)
			{
				return false;
			}

			return MyData.Load("USER_CURRENCY", out currency, out err);
		}

		private static bool hasItems(InventoryDto inventory, List<ItemCost> costs)
		{
			for (int i = 0; i < costs.Count; i++)
			{
				if (getItemCount(inventory, costs[i].itemId) < costs[i].count)
				{
					return false;
				}
			}

			return true;
		}

		private static int getItemCount(InventoryDto inventory, int itemId)
		{
			for (int i = 0; i < inventory.items.Count; i++)
			{
				OwnedItemDto item = inventory.items[i];
				if (item != null && item.itemId == itemId)
				{
					return item.count;
				}
			}

			return 0;
		}

		private static void addItem(InventoryDto inventory, int itemId, int count)
		{
			for (int i = 0; i < inventory.items.Count; i++)
			{
				OwnedItemDto item = inventory.items[i];
				if (item != null && item.itemId == itemId)
				{
					item.count += count;
					return;
				}
			}

			inventory.items.Add(makeItem(itemId, count));
		}

		private static OwnedItemDto makeItem(int itemId, int count)
		{
			OwnedItemDto item = new OwnedItemDto();
			item.itemId = itemId;
			item.count = count;
			return item;
		}

		private static OwnedItemDto[] toItemDto(List<ItemCost> costs)
		{
			OwnedItemDto[] result = new OwnedItemDto[costs.Count];
			for (int i = 0; i < costs.Count; i++)
			{
				result[i] = makeItem(costs[i].itemId, costs[i].count);
			}

			return result;
		}

		// 인벤토리 빈칸 수 — 장착 중이거나 보관함에 있는 장비는 칸을 차지하지 않는다(상점과 같은 기준).
		private static int getFreeInventorySlots(InventoryDto inventory)
		{
			int used = 0;
			for (int i = 0; i < inventory.equipments.Count; i++)
			{
				EquipmentInstanceDto equipment = inventory.equipments[i];
				if (equipment.equippedSlot == (int)EquipSlotTypes.None && equipment.inStash == false)
				{
					used++;
				}
			}

			return InventoryRules.GetInventoryCapacity(inventory.inventoryCapacityBonus) - used;
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

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
