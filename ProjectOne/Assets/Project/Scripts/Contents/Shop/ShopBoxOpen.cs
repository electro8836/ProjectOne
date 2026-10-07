using System;
using EDT;
using ProjectOne.Shared;
using ProjectOne.UserData;
using UnityEngine;

namespace ProjectOne.Shop
{
	// 보물상자 묶음 오픈 — 한 번에 몇 개를 열지와 자동분해 체크 상태.
	//
	// 슬롯의 버튼 숫자와 Presenter 가 보내는 개수가 같은 계산을 쓰도록 한 곳에 둔다.
	// 최종 판정은 서버(ShopBuy)가 같은 기준으로 한다.
	public static class ShopBoxOpen
	{
		// 자동분해 체크는 기기에 상품별로 저장한다.
		private const string DecomposeKeyPrefix = "ShopBoxDecompose_";

		public static bool IsDecomposeChecked(int goodsId)
		{
			return PlayerPrefs.GetInt(DecomposeKeyPrefix + goodsId, 0) != 0;
		}

		public static void SetDecomposeChecked(int goodsId, bool value)
		{
			PlayerPrefs.SetInt(DecomposeKeyPrefix + goodsId, value ? 1 : 0);
		}

		// 인벤토리 빈칸 수. 초과 보유 중이면 음수다.
		public static int GetFreeInventorySlots()
		{
			Inventory inventory = Account.Instance.Inventory;
			return inventory.InventoryCapacity - inventory.InventoryCount;
		}

		// 지금 누르면 열릴 상자 수. 열쇠가 모자라면 0.
		// 자동분해면 보유한 만큼 전부, 아니면 한 번에 여는 상한과 인벤토리 빈칸으로 자른다(상자 1개 = 1칸).
		// ignoreSpace 는 빈칸이 없을 때의 버튼 표시용이다 — 그 상태의 클릭은 열지 않고 경고만 띄운다.
		public static int GetOpenCount(Table_ShopGoods.Row row, bool decomposeAll, bool ignoreSpace)
		{
			int itemId;
			if (ShopRules.TryGetItemPrice(row, out itemId) == false)
			{
				return 0;
			}

			int count = Account.Instance.Inventory.GetCount(itemId) / row.Price;

			int remaining = ShopPurchaseCounter.GetRemaining(row);
			if (remaining != ShopPurchaseCounter.UNLIMITED)
			{
				count = Math.Min(count, remaining);
			}

			if (decomposeAll == true)
			{
				return count;
			}

			count = Math.Min(count, ShopRules.BoxOpenBatchMax);
			if (ignoreSpace == false)
			{
				count = Math.Min(count, GetFreeInventorySlots());
			}

			return Math.Max(count, 0);
		}
	}
}
