using System.Collections.Generic;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.UserData;

namespace ProjectOne.Upgrade
{
	// 전이가 거부된 이유. UI 가 버튼 상태와 문구를 이걸로 결정한다 (PetBook.GetEnhanceBlock 관례).
	public enum TransferBlock
	{
		None,
		Invalid,		// 인스턴스가 null 이거나 테이블 행이 없는 장비
		SameInstance,	// 같은 장비를 양쪽으로 지정
		SlotMismatch,	// 착용 슬롯 종류가 다름 (무기 ↔ 장갑 등)
		SameState,		// 등급·강화도가 이미 동일해 교체 의미가 없음
		NoTable,		// ItemTransfer 비용 행 없음
		NotEnough,		// 재화 부족
	}

	// 장비 전이 — 두 장비의 등급과 강화도를 서로 교체한다.
	//
	// 품질(quality)은 전이되지 않고 각자 그대로 남는다.
	// 등급과 강화도가 함께 움직이므로 교체 후에도 등급별 레벨 상한 정합성은 깨지지 않는다.
	// 비용은 두 장비의 등급 중 '높은 쪽' 행을 쓴다 — 저등급 장비를 끼워 비용을 깎지 못하게 한다.
	//
	// 지금은 로컬 권위다. 서버 이관은 STEP 14.
	public static class EquipmentTransfer
	{
		public static TransferBlock GetBlock(EquipmentInstance a, EquipmentInstance b)
		{
			if (a == null || b == null)
			{
				return TransferBlock.Invalid;
			}

			if (a.uid == b.uid)
			{
				return TransferBlock.SameInstance;
			}

			Table_Equipment.Row rowA = a.Equipment;
			Table_Equipment.Row rowB = b.Equipment;
			if (rowA == null || rowB == null)
			{
				return TransferBlock.Invalid;
			}

			if (rowA.EquipSlotType != rowB.EquipSlotType || rowA.EquipSlotType == EquipSlotTypes.None)
			{
				return TransferBlock.SlotMismatch;
			}

			if (a.grade == b.grade && a.level == b.level)
			{
				return TransferBlock.SameState;
			}

			Table_ItemTransfer.Row cost = findCost(rowA.EquipSlotType, a.grade, b.grade);
			if (cost == null)
			{
				return TransferBlock.NoTable;
			}

			if (CurrencyManager.Instance.GetAmount(cost.ReqCurrency) < cost.ReqCost)
			{
				return TransferBlock.NotEnough;
			}

			return TransferBlock.None;
		}

		// 전이 1회의 비용을 buffer 에 채운다. 행을 못 찾으면 비운다.
		public static void GetCost(EquipmentInstance a, EquipmentInstance b, List<UpgradeCost> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			if (a == null || b == null)
			{
				return;
			}

			Table_Equipment.Row rowA = a.Equipment;
			Table_Equipment.Row rowB = b.Equipment;
			if (rowA == null || rowB == null || rowA.EquipSlotType != rowB.EquipSlotType)
			{
				return;
			}

			Table_ItemTransfer.Row cost = findCost(rowA.EquipSlotType, a.grade, b.grade);
			if (cost == null || cost.ReqCurrency == EDT.Currency.None || cost.ReqCost <= 0)
			{
				return;
			}

			UpgradeCost entry;
			entry.currency = cost.ReqCurrency;
			entry.amount = cost.ReqCost;
			entry.owned = CurrencyManager.Instance.GetAmount(cost.ReqCurrency);
			buffer.Add(entry);
		}

		// 전이 실행 — 비용을 차감하고 두 장비의 등급·강화도를 교체한다.
		public static bool TryTransfer(EquipmentInstance a, EquipmentInstance b)
		{
			if (GetBlock(a, b) != TransferBlock.None)
			{
				return false;
			}

			List<UpgradeCost> costs = new List<UpgradeCost>(1);
			GetCost(a, b, costs);
			if (costs.Count == 0 || EquipmentUpgrade.TrySpend(costs) == false)
			{
				return false;
			}

			ItemGradeType grade = a.grade;
			int level = a.level;
			a.grade = b.grade;
			a.level = b.level;
			b.grade = grade;
			b.level = level;

			Account.Instance.Inventory.NotifyEquipmentChanged(a.uid);
			Account.Instance.Inventory.NotifyEquipmentChanged(b.uid);
			Account.Instance.Loadout.ReapplyEquipped(a.uid);
			Account.Instance.Loadout.ReapplyEquipped(b.uid);
			return true;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 두 등급 중 높은 쪽으로 비용 행을 찾는다.
		private static Table_ItemTransfer.Row findCost(EquipSlotTypes slot, ItemGradeType gradeA, ItemGradeType gradeB)
		{
			ItemGradeType higher = (int)gradeA >= (int)gradeB ? gradeA : gradeB;
			return EquipmentCatalog.GetTransfer(slot, higher);
		}
	}
}
