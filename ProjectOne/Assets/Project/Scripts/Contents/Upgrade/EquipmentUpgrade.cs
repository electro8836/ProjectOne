using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.UserData;

namespace ProjectOne.Upgrade
{
	// 비용 1건 — 재화. UI 가 부족분을 표시할 수 있도록 보유량도 함께 싣는다.
	// Craft 테이블 이관으로 강화 재료(강화주문서·승급석 등)가 아이템에서 재화로 승격되어
	// 아이템 비용은 더 이상 존재하지 않는다.
	public struct UpgradeCost
	{
		public EDT.Currency currency;
		public int amount;
		public int owned;

		public bool IsEnough
		{
			get { return owned >= amount; }
		}
	}

	// 승급이 거부된 이유.
	public enum PromoteBlock
	{
		None,
		Invalid,		// 인스턴스가 null 이거나 테이블 행이 없는 장비
		MaxGrade,		// 최대 등급 — 다음 등급이 없음
		NotMaxLevel,	// 현재 등급의 강화 레벨 상한에 도달하지 않음
	}

	// 장비 강화 · 승급 (아이템 설계 6장).
	//
	//   강화 : 레벨 +1. 상한은 현재 '등급' 의 ItemEnhanceTier.MaxLevel.
	//          비용 티어는 '올라갈 레벨(level + 1)' 이 속한 구간 — 등급이 아니다.
	//          두 조회를 혼동하면 조용히 틀린다.
	//   승급 : 등급 +1. 레벨·품질은 그대로 유지된다. 강화 레벨이 현재 등급 상한일 때만 가능하다.
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

			// 비용 티어는 '올라갈 레벨' 이 속한 구간이다 — 20레벨에서 강화하면 21이 되므로 Tier_2 를 낸다.
			int targetLevel = instance.level + 1;
			Table_ItemEnhanceTier.Row tier = EquipmentCatalog.GetTierByLevel(targetLevel);
			if (tier == null)
			{
				Debug.LogWarning($"[EquipmentUpgrade] 레벨 {targetLevel} 이 속한 강화 티어가 없습니다.");
				return;
			}

			Table_ItemEnhance.Row cost = EquipmentCatalog.GetEnhance(equipment.EquipSlotType, tier.ID);
			if (cost == null)
			{
				Debug.LogWarning($"[EquipmentUpgrade] 강화 비용 행 없음 — slot:{equipment.EquipSlotType} tier:{tier.ID}");
				return;
			}

			addCurrencyCost(buffer, cost.ReqCurrency_1, cost.ReqCost_1);
			addCurrencyCost(buffer, cost.ReqCurrency_2, cost.ReqCost_2);
		}

		// 강화 실행 — 비용을 실제로 차감하고 레벨을 +1 한다.
		public static bool TryEnhance(EquipmentInstance instance)
		{
			if (CanEnhance(instance) == false)
			{
				return false;
			}

			List<UpgradeCost> costs = new List<UpgradeCost>(2);
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

			Table_ItemPromotion.Row row = EquipmentCatalog.GetPromotion(equipment.EquipSlotType, instance.grade);
			if (row == null)
			{
				return ItemGradeType.None;
			}

			return row.ToGrade;
		}

		// 승급이 막힌 이유. UI 가 버튼 상태와 문구를 이걸로 결정한다 (TransferBlock 관례).
		// 승급은 다음 등급이 있고, 현재 등급의 강화 레벨 상한까지 올린 뒤에만 가능하다.
		public static PromoteBlock GetPromoteBlock(EquipmentInstance instance)
		{
			if (instance == null || instance.Equipment == null)
			{
				return PromoteBlock.Invalid;
			}

			if (GetNextGrade(instance) == ItemGradeType.None)
			{
				return PromoteBlock.MaxGrade;
			}

			if (instance.level < GetMaxLevel(instance))
			{
				return PromoteBlock.NotMaxLevel;
			}

			return PromoteBlock.None;
		}

		public static bool CanPromote(EquipmentInstance instance)
		{
			return GetPromoteBlock(instance) == PromoteBlock.None;
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

			Table_ItemPromotion.Row cost = EquipmentCatalog.GetPromotion(equipment.EquipSlotType, instance.grade);
			if (cost == null || cost.ToGrade == ItemGradeType.None)
			{
				return;
			}

			// 승급은 1회성이라 배율이 없다. 골드는 전용 컬럼(ReqGoldCount)으로 따로 붙는다.
			addCurrencyCost(buffer, cost.ReqCurrency_1, cost.ReqCost_1);
			addCurrencyCost(buffer, cost.ReqCurrency_2, cost.ReqCost_2);
			addCurrencyCost(buffer, EDT.Currency.Gold, cost.ReqGoldCount);
		}

		// 승급 실행 — 등급만 바꾼다. 레벨·품질은 유지된다 (설계 6.2).
		public static bool TryPromote(EquipmentInstance instance)
		{
			if (CanPromote(instance) == false)
			{
				return false;
			}

			ItemGradeType next = GetNextGrade(instance);

			List<UpgradeCost> costs = new List<UpgradeCost>(3);
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
				CurrencyManager.Instance.TrySpend(cost.currency, cost.amount);
			}

			return true;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void addCurrencyCost(List<UpgradeCost> buffer, EDT.Currency currency, int amount)
		{
			if (currency == EDT.Currency.None || amount <= 0)
			{
				return;
			}

			UpgradeCost cost;
			cost.currency = currency;
			cost.amount = amount;
			cost.owned = CurrencyManager.Instance.GetAmount(currency);
			buffer.Add(cost);
		}
	}
}
