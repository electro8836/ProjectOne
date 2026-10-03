using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 상점 — 모든 상품 구매. 구매 횟수 한도(일일 리셋 포함)는 USER_SHOP 에 저장하고 공유 ShopRules 로 판정한다.
	//
	// 보상 그룹은 던전 보상과 같이 서버가 공유 코어(RewardRoller)로 통째로 굴린다(같은 테이블·같은 규칙).
	// 히어로패스는 보상 그룹 대신 패스 활성화가 효과라 [임시] 경로로 따로 처리한다.
	public class ShopFunctions
	{
		public Stream ShopBuy()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				ShopBuyRequest req = JsonConvert.DeserializeObject<ShopBuyRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 1. 상품 + 구매 횟수 한도. 히어로패스는 시즌(35일)당 1회라 상점 횟수 대신 패스의 purchased 로 판정한다.
				Table_ShopGoods.Row goods = Table_ShopGoods.Get(req.goodsId);
				if (goods == null)
				{
					return FuncResult.Error("unknown goods: " + req.goodsId);
				}

				if (goods.GoodsType == EDT.GoodsType.HeroPass)
				{
					return buyHeroPass();
				}

				if (MyData.Load("USER_SHOP", out ShopDto shop, out string shopErr) == false)
				{
					return FuncResult.Error(shopErr);
				}

				int today = ResetDay.FromUtc(DateTime.UtcNow);
				if (ShopRules.GetRemaining(shop, goods, today) == 0)
				{
					return FuncResult.Error("purchase limit reached: " + req.goodsId);
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				// 2. 가격 차감 — 메모리에서만. 아래 단계가 실패하면 저장하지 않으므로 차감도 없던 일이 된다.
				if (trySpendPrice(goods, inventory, currency, out string priceErr) == false)
				{
					return FuncResult.Error(priceErr);
				}

				// 3. 추첨 → 반영. 상점 보상은 골드 보너스를 받지 않는다(보너스 0‰).
				//    보상 그룹이 없는 상품(광고 제거 등)은 구매 횟수만 남는다.
				RewardApplier applier = new RewardApplier(inventory, currency);
				if (goods.RewardGroupID > 0)
				{
					List<RolledReward> rolled = new List<RolledReward>();
					RewardRoller.Roll(goods.RewardGroupID, 0, new ServerRandomSource(), rolled, null);
					if (rolled.Count == 0)
					{
						return FuncResult.Error("goods rolled nothing: " + goods.RewardGroupID);
					}

					applier.ApplyAll(rolled);
				}

				ShopRules.Increase(shop, goods, today);

				// 4. 원자 저장
				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));
				tx.Add(TransactionValue.SetUpdate("USER_SHOP", new Where(), toParam(shop)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				ShopBuyResponse response = new ShopBuyResponse();
				response.success = true;
				response.rewards = applier.Granted;
				response.equipments = applier.Equipments;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 가격 유형별 차감. 재화·아이템만 실제로 차감한다.
		private static bool trySpendPrice(Table_ShopGoods.Row goods, InventoryDto inventory, CurrencyDto currency, out string err)
		{
			err = null;

			switch (goods.PriceType)
			{
				case EDT.PriceType.Item:
				{
					int itemId;
					if (ShopRules.TryGetItemPrice(goods, out itemId) == false)
					{
						err = "invalid price: " + goods.ID;
						return false;
					}

					if (InventoryUtil.TrySpendItem(inventory, itemId, goods.Price) == false)
					{
						err = "not enough item: " + itemId;
						return false;
					}

					return true;
				}

				case EDT.PriceType.Currency:
				{
					EDT.Currency priceCurrency;
					if (ShopRules.TryGetCurrencyPrice(goods, out priceCurrency) == false)
					{
						err = "invalid price: " + goods.ID;
						return false;
					}

					CurrencyCost cost = new CurrencyCost();
					cost.currency = priceCurrency;
					cost.amount = goods.Price;
					List<CurrencyCost> costs = new List<CurrencyCost>();
					costs.Add(cost);

					if (CurrencyUtil.TrySpendAll(currency, costs) == false)
					{
						err = "not enough currency: " + priceCurrency;
						return false;
					}

					return true;
				}

				case EDT.PriceType.Free:
					return true;

				// [임시] 광고 시청·인앱 결제 검증 없이 통과 — 광고 SDK·영수증 검증 연동 시 교체한다.
				case EDT.PriceType.Ad:
				case EDT.PriceType.Cash:
					return true;
			}

			err = "unsupported price type: " + goods.PriceType;
			return false;
		}

		// [임시] 히어로패스 구매 — 결제 검증 없이 이번 시즌 패스를 활성화한다. 결제 단계에서 영수증 검증으로 교체한다.
		// Load 가 EnsureSeason 을 먼저 적용하므로 새 시즌이면 purchased 가 풀려 다시 살 수 있다.
		private static Stream buyHeroPass()
		{
			if (HeroPassOps.Load(out HeroPassDto pass, out string passErr) == false)
			{
				return FuncResult.Error(passErr);
			}

			if (pass.purchased == true)
			{
				return FuncResult.Error("hero pass already purchased");
			}

			pass.purchased = true;

			List<TransactionValue> tx = new List<TransactionValue>();
			tx.Add(HeroPassOps.ToUpdate(pass));

			var txResult = Backend.GameData.TransactionWriteV2(tx);
			if (!txResult.IsSuccess())
			{
				return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
			}

			ShopBuyResponse response = new ShopBuyResponse();
			response.success = true;
			return FuncResult.Json(response);
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
