using System;
using System.Collections.Generic;
using System.Globalization;
using EDT;

namespace ProjectOne.Shared
{
	// 상점 규칙 — 구매 횟수 한도·일일 리셋·가격 해석. 클라·서버 공용.
	//
	// 하루 경계는 ResetDay(KST 06시)다. 서버는 서버 시간으로, 클라는 DailyReset.GetResetDay() 로 today 를 넘긴다.
	public static class ShopRules
	{
		// 무제한 상품의 남은 횟수 표기값.
		public const int UNLIMITED = -1;

		// 지금까지의 구매 횟수. 일일 리셋 상품이 날을 넘겼으면 0 으로 본다.
		public static int GetCount(ShopDto dto, Table_ShopGoods.Row row, int today)
		{
			ShopPurchaseDto entry = find(dto, row.ID);
			if (entry == null)
			{
				return 0;
			}

			if (row.UseDailyReset == true && entry.resetDay != today)
			{
				return 0;
			}

			return entry.count;
		}

		// 남은 구매 가능 횟수. 무제한 상품은 UNLIMITED 를 반환한다.
		public static int GetRemaining(ShopDto dto, Table_ShopGoods.Row row, int today)
		{
			if (row == null || row.MaxPurchaseCount <= 0)
			{
				return UNLIMITED;
			}

			int remaining = row.MaxPurchaseCount - GetCount(dto, row, today);
			return remaining > 0 ? remaining : 0;
		}

		// 1회 구매를 기록한다. 일일 리셋 상품이 날을 넘겼으면 0 부터 다시 센다.
		public static void Increase(ShopDto dto, Table_ShopGoods.Row row, int today)
		{
			ShopPurchaseDto entry = find(dto, row.ID);
			if (entry == null)
			{
				entry = new ShopPurchaseDto();
				entry.goodsId = row.ID;
				dto.purchases.Add(entry);
			}

			if (row.UseDailyReset == true && entry.resetDay != today)
			{
				entry.count = 0;
			}

			entry.resetDay = today;
			entry.count++;
		}

		// PriceType.Currency 의 PriceParam 은 재화 enum 이름이다.
		public static bool TryGetCurrencyPrice(Table_ShopGoods.Row row, out EDT.Currency currency)
		{
			currency = EDT.Currency.None;
			return row.PriceType == PriceType.Currency && row.Price > 0
				&& Enum.TryParse<EDT.Currency>(row.PriceParam, false, out currency) == true;
		}

		// PriceType.Item 의 PriceParam 은 아이템 ID 다(상자 열쇠 등).
		public static bool TryGetItemPrice(Table_ShopGoods.Row row, out int itemId)
		{
			itemId = 0;
			return row.PriceType == PriceType.Item && row.Price > 0
				&& int.TryParse(row.PriceParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) == true;
		}

		// 수집품(펫·코스튬) 상품이면 그 아이템 ID 를 돌려준다 — 보유 중이면 살 수 없다.
		// 보상 그룹의 유효한 Item 행 중 첫 수집품을 본다.
		public static bool TryGetCollectible(Table_ShopGoods.Row row, out int itemId)
		{
			itemId = 0;
			if (row == null || row.RewardGroupID <= 0)
			{
				return false;
			}

			IReadOnlyList<RewardTable.RewardEntry> entries = RewardTable.GetGroup(row.RewardGroupID);
			for (int i = 0; i < entries.Count; i++)
			{
				RewardTable.RewardEntry entry = entries[i];
				if (entry.isValid == true && entry.row.RewardType == RewardType.Item && CollectionRules.IsCollection(entry.itemId) == true)
				{
					itemId = entry.itemId;
					return true;
				}
			}

			return false;
		}

		private static ShopPurchaseDto find(ShopDto dto, int goodsId)
		{
			if (dto == null)
			{
				return null;
			}

			for (int i = 0; i < dto.purchases.Count; i++)
			{
				ShopPurchaseDto entry = dto.purchases[i];
				if (entry != null && entry.goodsId == goodsId)
				{
					return entry;
				}
			}

			return null;
		}
	}
}
