using System;
using System.Collections.Generic;
using EDT;
using ProjectOne.Utils;

namespace ProjectOne.Shop
{
	// 상품별 구매 횟수. MaxPurchaseCount 가 1 이상인 상품의 남은 횟수를 판정한다.
	//
	// TODO(서버) — 지금은 인메모리다. 구매가 서버 권위로 넘어가면 카운트도 서버가 소유해야 하고,
	// 일일 리셋 판정도 기기 시계가 아니라 서버 시간 기준이어야 한다. DungeonProgress 와 같은 처지다.
	public static class ShopPurchaseCounter
	{
		// 무제한 상품의 남은 횟수 표기값.
		public const int UNLIMITED = -1;

		private struct Record
		{
			public int count;
			public DateTime expireUtc;	// 일일 리셋 상품의 만료 시각. 리셋을 안 쓰면 DateTime.MaxValue
		}

		private static readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();

		// 지금까지의 구매 횟수. 일일 리셋 경계를 넘겼으면 0 으로 본다.
		public static int GetCount(int goodsId)
		{
			Record record;
			if (_records.TryGetValue(goodsId, out record) == false)
			{
				return 0;
			}

			if (DateTime.UtcNow >= record.expireUtc)
			{
				_records.Remove(goodsId);
				return 0;
			}

			return record.count;
		}

		// 남은 구매 가능 횟수. 무제한 상품은 UNLIMITED 를 반환한다.
		public static int GetRemaining(Table_ShopGoods.Row row)
		{
			if (row == null || row.MaxPurchaseCount <= 0)
			{
				return UNLIMITED;
			}

			int remaining = row.MaxPurchaseCount - GetCount(row.ID);
			return remaining > 0 ? remaining : 0;
		}

		public static bool CanPurchase(Table_ShopGoods.Row row)
		{
			return GetRemaining(row) != 0;
		}

		public static void Increase(int goodsId, bool useDailyReset)
		{
			Record record;
			if (_records.TryGetValue(goodsId, out record) == false || DateTime.UtcNow >= record.expireUtc)
			{
				record.count = 0;

				// 다음 갱신 경계를 기록해 둔다 — 그 시각을 넘긴 기록은 조회 시 0 으로 간주된다.
				record.expireUtc = useDailyReset ? DateTime.UtcNow + DailyReset.GetRemaining() : DateTime.MaxValue;
			}

			record.count++;
			_records[goodsId] = record;
		}
	}
}
