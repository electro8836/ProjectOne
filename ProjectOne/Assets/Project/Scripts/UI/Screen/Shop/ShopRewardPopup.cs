using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using UnityEngine;
using UnityEngine.UIElements;

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

		// UI Toolkit 칸에서 연다 — 설명만 있는 대상은 누른 칸 위에 툴팁으로 띄운다.
		public static void Show(RewardPreviewItem item, VisualElement anchor, CancellationToken ct)
		{
			string name;
			string desc;
			if (tryGetSimpleText(item, out name, out desc) == true)
			{
				UIManager.Instance.ShowTooltipAsync(name, desc, anchor, ct).Forget();
				return;
			}

			showFull(item, ct);
		}

		public static void Show(RewardPreviewItem item, RectTransform anchor, CancellationToken ct)
		{
			string name;
			string desc;
			if (tryGetSimpleText(item, out name, out desc) == true)
			{
				showSimple(name, desc, anchor, ct);
				return;
			}

			showFull(item, ct);
		}

		// 재화·재료·수집품(펫·코스튬)은 보여줄 것이 이름·설명뿐이라 정식 팝업을 열지 않는다.
		private static bool tryGetSimpleText(RewardPreviewItem item, out string name, out string desc)
		{
			name = string.Empty;
			desc = string.Empty;

			if (item.equipment != null)
			{
				return false;
			}

			if (item.type == RewardType.Currency)
			{
				Table_Currency.Row currency = Table_Currency.Get(item.currency);
				if (currency == null)
				{
					return false;
				}

				name = currency.Name;
				desc = currency.Desc;
				return true;
			}

			Table_Item.Row itemRow = Table_Item.Get(item.itemId);
			if (itemRow != null && (itemRow.MainCategory == ItemMainCategory.Material || itemRow.MainCategory == ItemMainCategory.Collection))
			{
				name = itemRow.Name;
				desc = itemRow.Desc;
				return true;
			}

			return false;
		}

		// 장비·소모품의 정식 읽기 전용 팝업.
		private static void showFull(RewardPreviewItem item, CancellationToken ct)
		{
			// 장비는 보유하지 않은 표시용 인스턴스라 uid 가 아니라 인스턴스를 그대로 넘긴다.
			if (item.equipment != null)
			{
				UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, item.equipment, ct).Forget();
				return;
			}

			if (item.type == RewardType.Currency || Table_Item.Get(item.itemId) == null)
			{
				return;
			}

			UIManager.Instance.ShowConsumablePopupAsync(CONSUMABLE_POPUP_ADDRESS, item.itemId, true, ct).Forget();
		}

		private static void showSimple(string name, string desc, RectTransform anchor, CancellationToken ct)
		{
			UIManager.Instance.ShowSimplePopupAsync(name + "\n" + desc, anchor, ct).Forget();
		}
	}
}
