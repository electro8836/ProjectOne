using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 상점 — 지금은 보물상자(GoodsType.Box) 개봉만 처리한다. 다른 상품은 거절한다.
	//
	// 상자 보상 그룹은 던전 보상과 같이 서버가 공유 코어(RewardRoller)로 통째로 굴린다(같은 테이블·같은 규칙).
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

				// 1. 상품 — 열쇠(아이템) 가격의 보물상자만 받는다.
				Table_ShopGoods.Row goods = Table_ShopGoods.Get(req.goodsId);
				if (goods == null || goods.GoodsType != EDT.GoodsType.Box)
				{
					return FuncResult.Error("unsupported goods: " + req.goodsId);
				}

				int keyItemId;
				if (goods.PriceType != EDT.PriceType.Item || goods.Price <= 0
					|| int.TryParse(goods.PriceParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out keyItemId) == false)
				{
					return FuncResult.Error("invalid price: " + req.goodsId);
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
				if (InventoryUtil.TrySpendItem(inventory, keyItemId, goods.Price) == false)
				{
					return FuncResult.Error("not enough key: " + keyItemId);
				}

				// 3. 추첨 → 반영. 상자 보상은 골드 보너스를 받지 않는다(보너스 0‰).
				List<RolledReward> rolled = new List<RolledReward>();
				RewardRoller.Roll(goods.RewardGroupID, 0, new ServerRandomSource(), rolled, null);
				if (rolled.Count == 0)
				{
					return FuncResult.Error("box rolled nothing: " + goods.RewardGroupID);
				}

				RewardApplier applier = new RewardApplier(inventory, currency);
				applier.ApplyAll(rolled);

				// 4. 원자 저장
				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

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

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
