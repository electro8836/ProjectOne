using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Currency;

namespace ProjectOne.UserData
{
	// 강화/제작 로컬 실행 — 클라에서 확률 롤·재화/재료 차감·획득 처리 후 응답 반환.
	// 추후 서버 구현(IUpgradeService)으로 교체하면 표시용 조회(UpgradeSystem)는 그대로 둔다.
	public sealed class LocalUpgradeService : IUpgradeService
	{
		public UniTask<EnchantResponse> RequestEnchant(int itemId)
		{
			EnchantPreview preview = UpgradeSystem.GetEnchantPreview(itemId);
			if (preview.Valid == false)
			{
				return UniTask.FromResult(new EnchantResponse(UpgradeResult.InvalidTarget, itemId, 0));
			}

			if (preview.IsMax == true)
			{
				return UniTask.FromResult(new EnchantResponse(UpgradeResult.MaxLevel, itemId, preview.CurrentLevel));
			}

			if (preview.CanAfford == false)
			{
				return UniTask.FromResult(new EnchantResponse(UpgradeResult.NotEnoughCurrency, itemId, preview.CurrentLevel));
			}

			// 재화 차감 (비용 0 이면 차감 생략)
			if (preview.Cost > 0)
			{
				CurrencyManager.Instance.TrySpend(preview.CostType, preview.Cost);
			}

			// 확률 롤 — 실패해도 재화는 이미 소모, 레벨 유지
			if (Random.value > preview.Probability)
			{
				return UniTask.FromResult(new EnchantResponse(UpgradeResult.Fail, itemId, preview.CurrentLevel));
			}

			int newLevel = preview.CurrentLevel + 1;
			Account.Instance.Inventory.SetEnhanceLevel(itemId, newLevel);
			Account.Instance.Loadout.ReapplyEquipped(itemId);
			return UniTask.FromResult(new EnchantResponse(UpgradeResult.Success, itemId, newLevel));
		}

		public UniTask<CraftResponse> RequestCraft(int craftId)
		{
			Table_Craft.Row craft = Table_Craft.Get(craftId);
			if (craft == null || craft.Result_EquipmentID <= 0)
			{
				return UniTask.FromResult(new CraftResponse(UpgradeResult.InvalidTarget, 0));
			}

			if (UpgradeSystem.HasCraftCost(craft) == false)
			{
				return UniTask.FromResult(new CraftResponse(UpgradeResult.NotEnoughCurrency, craft.Result_EquipmentID));
			}

			if (UpgradeSystem.HasCraftMaterials(craft) == false)
			{
				return UniTask.FromResult(new CraftResponse(UpgradeResult.NotEnoughMaterial, craft.Result_EquipmentID));
			}

			// 검증 통과 후 일괄 차감 (재화 → 재료) 한 뒤 결과 아이템 획득
			if (craft.CurrencyType_01 != CurrencyInfo.None && craft.CurrencyCost_01 > 0)
			{
				CurrencyManager.Instance.TrySpend(craft.CurrencyType_01, craft.CurrencyCost_01);
			}

			if (craft.CurrencyType_02 != CurrencyInfo.None && craft.CurrencyCost_02 > 0)
			{
				CurrencyManager.Instance.TrySpend(craft.CurrencyType_02, craft.CurrencyCost_02);
			}

			spendMaterial(craft.Material_01_ID, craft.Material_01_Count);
			spendMaterial(craft.Material_02_ID, craft.Material_02_Count);
			spendMaterial(craft.Material_03_ID, craft.Material_03_Count);

			Account.Instance.Inventory.Add(craft.Result_EquipmentID, 1);
			return UniTask.FromResult(new CraftResponse(UpgradeResult.Success, craft.Result_EquipmentID));
		}

		private static void spendMaterial(int itemId, int count)
		{
			if (itemId <= 0 || count <= 0)
			{
				return;
			}

			Account.Instance.Inventory.TrySpend(itemId, count);
		}
	}
}
