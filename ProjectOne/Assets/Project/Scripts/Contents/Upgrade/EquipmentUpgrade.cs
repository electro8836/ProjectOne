using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.UserData;

namespace ProjectOne.Upgrade
{
	// 비용 1건 — 아이템 또는 재화. UI 가 부족분을 표시할 수 있도록 보유량도 함께 싣는다.
	public struct UpgradeCost
	{
		public int itemId;					// > 0 이면 아이템 비용
		public EDT.Currency currency;		// None 이 아니면 재화 비용
		public int amount;
		public int owned;

		public bool IsEnough
		{
			get { return owned >= amount; }
		}
	}

	// 장비 강화 · 승급 (아이템 설계 6장).
	//
	//   강화 : 레벨 +1. 상한은 현재 '등급' 의 EquipEnhanceTier.MaxLevel.
	//          비용 티어는 현재 '레벨' 이 속한 구간 — 등급이 아니다. 두 조회를 혼동하면 조용히 틀린다.
	//   승급 : 등급 +1. 레벨·순도·품질은 그대로 유지된다.
	//
	// 지금은 로컬 권위다. 서버 이관은 STEP 14.
	public static class EquipmentUpgrade
	{
		// ── 강화 ──────────────────────────────────────────────────────

		// 현재 등급 기준 강화 레벨 상한. 0 이면 티어 데이터가 없어 강화 불가.
		public static int GetMaxLevel(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return 0;
			}

			return EquipmentCatalog.GetMaxLevel(instance.grade);
		}

		public static bool CanEnhance(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return false;
			}

			int max = GetMaxLevel(instance);
			return max > 0 && instance.level < max;
		}

		// 다음 강화 1회의 비용을 buffer 에 채운다. 티어를 못 찾으면 비운다.
		public static void GetEnhanceCost(EquipmentInstance instance, List<UpgradeCost> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			if (instance == null)
			{
				return;
			}

			Table_Equipment.Row equipment = instance.Equipment;
			if (equipment == null)
			{
				return;
			}

			// 비용 티어는 '현재 레벨' 이 속한 구간이다 (설계 6.1).
			Table_EquipEnhanceTier.Row tier = EquipmentCatalog.GetTierByLevel(instance.level);
			if (tier == null)
			{
				Debug.LogWarning($"[EquipmentUpgrade] 레벨 {instance.level} 이 속한 강화 티어가 없습니다.");
				return;
			}

			Table_EquipEnhance.Row cost = EquipmentCatalog.GetEnhance(equipment.EquipSlotType, tier.ID);
			if (cost == null)
			{
				Debug.LogWarning($"[EquipmentUpgrade] 강화 비용 행 없음 — slot:{equipment.EquipSlotType} tier:{tier.ID}");
				return;
			}

			// n 은 구간 내 상대 레벨. 티어가 바뀌면 0으로 리셋된다.
			int n = instance.level - tier.MinLevel;
			if (n < 0)
			{
				n = 0;
			}

			addItemCost(buffer, cost.CostItemID_1, scale(cost.CostItemCnt_1, cost.CostItemMult_1, n));
			addItemCost(buffer, cost.CostItemID_2, scale(cost.CostItemCnt_2, cost.CostItemMult_2, n));
			addCurrencyCost(buffer, cost.CostCurrencyID, scale(cost.CostCurrencyValue, cost.CostCurrencyMult, n));
		}

		// 강화 실행 — 비용을 실제로 차감하고 레벨을 +1 한다.
		public static bool TryEnhance(EquipmentInstance instance)
		{
			if (CanEnhance(instance) == false)
			{
				return false;
			}

			List<UpgradeCost> costs = new List<UpgradeCost>(4);
			GetEnhanceCost(instance, costs);
			if (costs.Count == 0 || TrySpend(costs) == false)
			{
				return false;
			}

			instance.level++;
			Account.Instance.Inventory.NotifyEquipmentChanged(instance.uid);
			Account.Instance.Loadout.ReapplyEquipped(instance.uid);
			return true;
		}

		// ── 승급 ──────────────────────────────────────────────────────

		// 승급 후 등급. None 이면 승급 불가 (Mythic 이거나 MaxGrade 도달).
		public static ItemGradeType GetNextGrade(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return ItemGradeType.None;
			}

			Table_Equipment.Row equipment = instance.Equipment;
			if (equipment == null)
			{
				return ItemGradeType.None;
			}

			if ((int)instance.grade >= (int)equipment.MaxGrade)
			{
				return ItemGradeType.None;
			}

			Table_EquipPromotion.Row row = EquipmentCatalog.GetPromotion(equipment.EquipSlotType, instance.grade);
			if (row == null)
			{
				return ItemGradeType.None;
			}

			return row.ToGrade;
		}

		public static bool CanPromote(EquipmentInstance instance)
		{
			return GetNextGrade(instance) != ItemGradeType.None;
		}

		public static void GetPromoteCost(EquipmentInstance instance, List<UpgradeCost> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			if (instance == null)
			{
				return;
			}

			Table_Equipment.Row equipment = instance.Equipment;
			if (equipment == null)
			{
				return;
			}

			Table_EquipPromotion.Row cost = EquipmentCatalog.GetPromotion(equipment.EquipSlotType, instance.grade);
			if (cost == null || cost.ToGrade == ItemGradeType.None)
			{
				return;
			}

			// 승급은 1회성이라 배율이 없다 (설계 3.7).
			addItemCost(buffer, cost.CostItemID_1, cost.CostItemCnt_1);
			addItemCost(buffer, cost.CostItemID_2, cost.CostItemCnt_2);
			addItemCost(buffer, cost.CostItemID_3, cost.CostItemCnt_3);
			addItemCost(buffer, cost.CostItemID_4, cost.CostItemCnt_4);
			addCurrencyCost(buffer, cost.CostCurrencyID, cost.CostCurrencyValue);
		}

		// 승급 실행 — 등급만 바꾼다. 레벨·순도·품질은 유지된다 (설계 6.2).
		public static bool TryPromote(EquipmentInstance instance)
		{
			ItemGradeType next = GetNextGrade(instance);
			if (next == ItemGradeType.None)
			{
				return false;
			}

			List<UpgradeCost> costs = new List<UpgradeCost>(6);
			GetPromoteCost(instance, costs);
			if (costs.Count == 0 || TrySpend(costs) == false)
			{
				return false;
			}

			instance.grade = next;
			Account.Instance.Inventory.NotifyEquipmentChanged(instance.uid);
			Account.Instance.Loadout.ReapplyEquipped(instance.uid);
			return true;
		}

		// ── 비용 ──────────────────────────────────────────────────────

		public static bool IsAffordable(List<UpgradeCost> costs)
		{
			if (costs == null || costs.Count == 0)
			{
				return false;
			}

			for (int i = 0; i < costs.Count; i++)
			{
				if (costs[i].IsEnough == false)
				{
					return false;
				}
			}

			return true;
		}

		// 전부 충분할 때만 차감한다 — 부분 차감이 남지 않도록 검사를 먼저 끝낸다.
		public static bool TrySpend(List<UpgradeCost> costs)
		{
			if (IsAffordable(costs) == false)
			{
				return false;
			}

			for (int i = 0; i < costs.Count; i++)
			{
				UpgradeCost cost = costs[i];
				if (cost.itemId > 0)
				{
					Account.Instance.Inventory.TrySpend(cost.itemId, cost.amount);
				}
				else if (cost.currency != EDT.Currency.None)
				{
					CurrencyManager.Instance.TrySpend(cost.currency, cost.amount);
				}
			}

			return true;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static int scale(int baseValue, float mult, int n)
		{
			if (baseValue <= 0)
			{
				return 0;
			}

			return Mathf.RoundToInt(baseValue * (1f + mult * n));
		}

		private static void addItemCost(List<UpgradeCost> buffer, int itemId, int amount)
		{
			if (itemId <= 0 || amount <= 0)
			{
				return;
			}

			UpgradeCost cost;
			cost.itemId = itemId;
			cost.currency = EDT.Currency.None;
			cost.amount = amount;
			cost.owned = Account.Instance.Inventory.GetCount(itemId);
			buffer.Add(cost);
		}

		private static void addCurrencyCost(List<UpgradeCost> buffer, EDT.Currency currency, int amount)
		{
			if (currency == EDT.Currency.None || amount <= 0)
			{
				return;
			}

			UpgradeCost cost;
			cost.itemId = 0;
			cost.currency = currency;
			cost.amount = amount;
			cost.owned = CurrencyManager.Instance.GetAmount(currency);
			buffer.Add(cost);
		}
	}
}
