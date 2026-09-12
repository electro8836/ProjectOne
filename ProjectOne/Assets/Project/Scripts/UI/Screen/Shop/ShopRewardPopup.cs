using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using UnityEngine;

namespace ProjectOne.UI
{
	// 상점에 진열된 것을 눌렀을 때 여는 팝업.
	//
	// 상점의 목록은 전부 아직 내 것이 아니다 — 던전 결과창과 같은 **읽기 전용** 경로만 연다.
	// 장착·사용·수량조절 같은 조작은 팝업 쪽에서 전부 막힌다.
	public static class ShopRewardPopup
	{
		private const string EQUIPMENT_POPUP_ADDRESS = "UIPrefab_EquipmentPopup";
		private const string CONSUMABLE_POPUP_ADDRESS = "UIPrefab_ConsumablePopup";

		public static void Show(RewardPreviewItem item, ItemSlot anchor, CancellationToken ct)
		{
			// 장비는 보유하지 않은 표시용 인스턴스라 uid 가 아니라 인스턴스를 그대로 넘긴다.
			if (item.equipment != null)
			{
				UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, item.equipment, ct).Forget();
				return;
			}

			if (item.type == RewardType.Currency)
			{
				Table_Currency.Row currency = Table_Currency.Get(item.currency);
				if (currency != null)
				{
					showSimple(currency.Name, currency.Desc, anchor, ct);
				}

				return;
			}

			Table_Item.Row itemRow = Table_Item.Get(item.itemId);
			if (itemRow == null)
			{
				return;
			}

			// 재료는 보여줄 것이 이름·설명뿐이라 정식 팝업을 열지 않는다.
			if (itemRow.MainCategory == ItemMainCategory.Material)
			{
				showSimple(itemRow.Name, itemRow.Desc, anchor, ct);
				return;
			}

			UIManager.Instance.ShowConsumablePopupAsync(CONSUMABLE_POPUP_ADDRESS, item.itemId, true, ct).Forget();
		}

		private static void showSimple(string name, string desc, ItemSlot anchor, CancellationToken ct)
		{
			UIManager.Instance.ShowSimplePopupAsync(name + "\n" + desc, anchor.transform as RectTransform, ct).Forget();
		}
	}
}
