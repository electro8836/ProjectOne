using System.Collections.Generic;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.Upgrade;
using ProjectOne.UserData;
using UnityEngine;

namespace ProjectOne.Forge
{
	// 재료 비용 1건 — UI 가 부족분을 표시할 수 있도록 보유량도 함께 싣는다.
	public struct ForgeItemCost
	{
		public int itemId;
		public int count;
		public int owned;

		public bool IsEnough
		{
			get { return owned >= count; }
		}
	}

	// 제작·재련의 클라 쪽 입구 (제작시스템 설계 3·4장).
	//
	// 판정·비용은 서버와 같은 공유 규칙(ForgeRules)에 보유량을 붙여 돌려준다.
	// 실행은 서버 권위다 — 확률은 서버가 굴리고, 클라는 응답을 Apply* 로 반영한다.
	public static class ForgeService
	{
		// 공유 규칙 버퍼
		private static readonly List<ItemCost> _rawItems = new List<ItemCost>(4);
		private static readonly List<CurrencyCost> _rawCurrencies = new List<CurrencyCost>(2);

		// ── 제작 ──────────────────────────────────────────────────────

		public static void GetItemCosts(Table_ForgeRecipe.Row recipe, int count, List<ForgeItemCost> buffer)
		{
			buffer.Clear();
			if (recipe == null)
			{
				return;
			}

			ForgeRules.GetItemCosts(recipe, count, _rawItems);
			for (int i = 0; i < _rawItems.Count; i++)
			{
				ForgeItemCost cost;
				cost.itemId = _rawItems[i].itemId;
				cost.count = _rawItems[i].count;
				cost.owned = Account.Instance.Inventory.GetCount(_rawItems[i].itemId);
				buffer.Add(cost);
			}
		}

		public static void GetCurrencyCosts(Table_ForgeRecipe.Row recipe, int count, List<UpgradeCost> buffer)
		{
			buffer.Clear();
			if (recipe == null)
			{
				return;
			}

			ForgeRules.GetCurrencyCosts(recipe, count, _rawCurrencies);
			EquipmentUpgrade.FillCosts(_rawCurrencies, buffer);
		}

		// 제작이 막힌 이유. source 는 티어 올리기의 이전 장비(다른 종류는 null).
		public static ForgeBlock GetBlock(Table_ForgeRecipe.Row recipe, EquipmentInstance source, int count)
		{
			ForgeRecipeKind kind = ForgeRules.GetKind(recipe);
			if (kind == ForgeRecipeKind.Invalid || count < 1)
			{
				return ForgeBlock.Invalid;
			}

			if (kind == ForgeRecipeKind.Upgrade)
			{
				if (source == null)
				{
					return ForgeBlock.NoSource;
				}

				if (source.itemId != recipe.SourceItemID)
				{
					return ForgeBlock.SourceMismatch;
				}
			}
			else if (kind == ForgeRecipeKind.Base && Account.Instance.Inventory.IsInventoryFull == true)
			{
				return ForgeBlock.InventoryFull;
			}

			ForgeRules.GetItemCosts(recipe, count, _rawItems);
			for (int i = 0; i < _rawItems.Count; i++)
			{
				if (Account.Instance.Inventory.GetCount(_rawItems[i].itemId) < _rawItems[i].count)
				{
					return ForgeBlock.NotEnoughItems;
				}
			}

			ForgeRules.GetCurrencyCosts(recipe, count, _rawCurrencies);
			for (int i = 0; i < _rawCurrencies.Count; i++)
			{
				if (CurrencyManager.Instance.GetAmount(_rawCurrencies[i].currency) < _rawCurrencies[i].amount)
				{
					return ForgeBlock.NotEnoughCurrency;
				}
			}

			return ForgeBlock.None;
		}

		// 지금 성공 확률(‰). 티어 올리기는 이전 장비의 연속 실패 횟수만큼 보정된다.
		public static int GetSuccessRate(Table_ForgeRecipe.Row recipe, EquipmentInstance source)
		{
			int fails = (source != null) ? source.forgeFails : 0;
			return ForgeRules.GetSuccessRate(recipe, fails);
		}

		// 재료 조합을 지금 몇 번 할 수 있는지 (재료·재화 보유량 기준, 최대 MaxMaterialCount).
		public static int GetMaxCraftable(Table_ForgeRecipe.Row recipe)
		{
			if (recipe == null)
			{
				return 0;
			}

			int max = ForgeRules.MaxMaterialCount;
			ForgeRules.GetItemCosts(recipe, 1, _rawItems);
			for (int i = 0; i < _rawItems.Count; i++)
			{
				int can = Account.Instance.Inventory.GetCount(_rawItems[i].itemId) / _rawItems[i].count;
				if (can < max)
				{
					max = can;
				}
			}

			ForgeRules.GetCurrencyCosts(recipe, 1, _rawCurrencies);
			for (int i = 0; i < _rawCurrencies.Count; i++)
			{
				int can = CurrencyManager.Instance.GetAmount(_rawCurrencies[i].currency) / _rawCurrencies[i].amount;
				if (can < max)
				{
					max = can;
				}
			}

			return max;
		}

		// 제작 응답 — 차감·획득량을 반영하고, 장비는 서버 최종 상태로 덮거나 새로 넣는다.
		public static void ApplyCraftResponse(ForgeCraftResponse response)
		{
			spendCurrencies(response.spent);

			if (response.spentItems != null)
			{
				for (int i = 0; i < response.spentItems.Length; i++)
				{
					OwnedItemDto spent = response.spentItems[i];
					if (Account.Instance.Inventory.TrySpend(spent.itemId, spent.count) == false)
					{
						Debug.LogWarning($"[ForgeService] 서버 차감분이 로컬 보유량보다 큽니다 — item:{spent.itemId} count:{spent.count}");
					}
				}
			}

			if (response.gainedItems != null)
			{
				for (int i = 0; i < response.gainedItems.Length; i++)
				{
					Account.Instance.Inventory.Add(response.gainedItems[i].itemId, response.gainedItems[i].count);
				}
			}

			if (response.equipment == null)
			{
				return;
			}

			EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(response.equipment.uid);
			if (instance == null)
			{
				// 기초 제작 — 서버가 채번한 UID 그대로 넣는다. 잠금은 서버가 이미 저장했으므로 더티로 만들지 않는다.
				instance = RewardGranter.CreateServerEquipment(response.equipment);
				if (instance == null)
				{
					Debug.LogWarning($"[ForgeService] 제작 장비를 만들 수 없습니다 — item:{response.equipment.itemId}");
					return;
				}

				instance.locked = response.equipment.locked;
				Account.Instance.Inventory.AddEquipment(instance);
				return;
			}

			applyEquipment(instance, response.equipment);
		}

		// ── 재련 ──────────────────────────────────────────────────────

		public static ReforgeBlock GetReforgeBlock(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return ReforgeBlock.Invalid;
			}

			return ForgeRules.GetReforgeBlock(instance.itemId, instance.grade, instance.quality);
		}

		public static void GetReforgeCosts(EquipmentInstance instance, List<UpgradeCost> buffer)
		{
			buffer.Clear();
			if (instance == null)
			{
				return;
			}

			ForgeRules.GetReforgeCosts(instance.grade, _rawCurrencies);
			EquipmentUpgrade.FillCosts(_rawCurrencies, buffer);
		}

		public static void ApplyReforgeResponse(ForgeReforgeResponse response)
		{
			spendCurrencies(response.spent);
			if (response.equipment == null)
			{
				return;
			}

			EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(response.equipment.uid);
			if (instance == null)
			{
				Debug.LogWarning($"[ForgeService] 응답 장비를 찾을 수 없습니다 — uid:{response.equipment.uid}");
				return;
			}

			applyEquipment(instance, response.equipment);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void spendCurrencies(CurrencyAmountDto[] spent)
		{
			if (spent == null)
			{
				return;
			}

			for (int i = 0; i < spent.Length; i++)
			{
				if (CurrencyManager.Instance.TrySpend((EDT.Currency)spent[i].currencyId, spent[i].amount) == false)
				{
					Debug.LogWarning($"[ForgeService] 서버 차감분이 로컬 보유량보다 큽니다 — currency:{spent[i].currencyId} amount:{spent[i].amount}");
				}
			}
		}

		// 서버 최종 상태로 덮는다. 티어가 오르면 장비 행이 바뀌므로 장착 중이면 스탯·평타·외형을 다시 적용한다.
		private static void applyEquipment(EquipmentInstance instance, EquipmentInstanceDto dto)
		{
			instance.itemId = dto.itemId;
			instance.grade = (ItemGradeType)dto.grade;
			instance.level = dto.level;
			instance.quality = dto.quality;
			instance.forgeFails = dto.forgeFails;
			instance.locked = dto.locked;

			Account.Instance.Inventory.NotifyEquipmentChanged(instance.uid);
			Account.Instance.Loadout.ReapplyEquipped(instance.uid);
		}
	}
}
