using EDT;
using ProjectOne.Shared;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Shop
{
	// 상품별 구매 횟수. MaxPurchaseCount 가 1 이상인 상품의 남은 횟수를 판정한다.
	//
	// 원본은 서버(USER_SHOP)다 — 로그인 때 Set 으로 받고, 구매 성공 응답 후 같은 규칙(ShopRules)으로 따라 센다.
	// 일일 리셋 경계는 서버와 같은 ResetDay(KST 06시)다.
	public static class ShopPurchaseCounter
	{
		// 무제한 상품의 남은 횟수 표기값.
		public const int UNLIMITED = ShopRules.UNLIMITED;

		private static ShopDto _dto = new ShopDto();

		public static void Set(ShopDto dto)
		{
			_dto = dto;
		}

		// 남은 구매 가능 횟수. 무제한 상품은 UNLIMITED 를 반환한다.
		// 히어로패스는 시즌(35일)당 1회라 상점 횟수 대신 이번 시즌 구매 여부로 판정한다(서버와 같은 기준).
		public static int GetRemaining(Table_ShopGoods.Row row)
		{
			if (row != null && row.GoodsType == GoodsType.HeroPass)
			{
				return Account.Instance.HeroPass.IsPurchased ? 0 : 1;
			}

			// 수집품(펫·코스튬)은 이미 가지고 있으면 살 수 없다 — 다른 경로(필드보스 등)로 얻었어도 같다.
			if (isOwnedCollectible(row) == true)
			{
				return 0;
			}

			return ShopRules.GetRemaining(_dto, row, DailyReset.GetResetDay());
		}

		private static bool isOwnedCollectible(Table_ShopGoods.Row row)
		{
			int itemId;
			if (ShopRules.TryGetCollectible(row, out itemId) == false)
			{
				return false;
			}

			if (CollectionRules.IsPet(itemId) == true)
			{
				return Account.Instance.Pet.IsOwned(itemId);
			}

			return Account.Instance.Costume.IsOwned(itemId);
		}

		public static bool CanPurchase(Table_ShopGoods.Row row)
		{
			return GetRemaining(row) != 0;
		}

		public static void Increase(Table_ShopGoods.Row row)
		{
			ShopRules.Increase(_dto, row, DailyReset.GetResetDay());
		}
	}
}
