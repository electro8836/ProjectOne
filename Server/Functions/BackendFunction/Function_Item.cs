using System;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 스택 아이템 차감 — 인벤토리 파괴. 클라는 이미 로컬에서 뺐다.
	// 장비·수집품은 인스턴스·보유 개념이라 여기서 다루지 않는다.
	public class ItemFunctions
	{
		public Stream ItemSpend()
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

				ItemSpendRequest req = JsonConvert.DeserializeObject<ItemSpendRequest>(Backend.Content["req"].ToString());
				if (req == null || req.items == null || req.items.Length == 0)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				// 하나라도 어긋나면 전체를 거절한다 — 저장하지 않으므로 앞에서 뺀 것도 없던 일이 된다.
				for (int i = 0; i < req.items.Length; i++)
				{
					OwnedItemDto item = req.items[i];
					if (item == null || item.count <= 0)
					{
						return FuncResult.Error("invalid count");
					}

					Table_Item.Row row = Table_Item.Get(item.itemId);
					if (row == null || row.MainCategory == ItemMainCategory.Equipment || row.MainCategory == ItemMainCategory.Collection)
					{
						return FuncResult.Error("not a stack item: " + item.itemId);
					}

					if (InventoryUtil.TrySpendItem(inventory, item.itemId, item.count) == false)
					{
						return FuncResult.Error("not enough item: " + item.itemId);
					}
				}

				Param param = new Param();
				param.Add("Data", JsonConvert.SerializeObject(inventory));
				var updateResult = Backend.GameData.Update("USER_INVENTORY", new Where(), param);
				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("USER_INVENTORY Update Failed: " + updateResult.GetErrorCode());
				}

				ItemSpendResponse response = new ItemSpendResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}
	}
}
