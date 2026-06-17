using EDT;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ProjectOne.Currency;

namespace ProjectOne.UserData
{
	// 강화 미리보기(표시용) — 클라가 항상 계산. 다음 레벨 기준 비용/확률/가능 여부.
	public readonly struct EnchantPreview
	{
		public readonly bool Valid;         // 강화 가능 대상(테이블+보유)
		public readonly int CurrentLevel;
		public readonly int MaxLevel;
		public readonly bool IsMax;
		public readonly CurrencyInfo CostType;
		public readonly int Cost;           // 다음 레벨 강화 비용
		public readonly float Probability;  // 다음 레벨 성공 확률(0~1)
		public readonly bool CanAfford;     // 재화 충분 여부

		public EnchantPreview(bool valid, int currentLevel, int maxLevel, bool isMax, CurrencyInfo costType, int cost, float probability, bool canAfford)
		{
			Valid = valid;
			CurrentLevel = currentLevel;
			MaxLevel = maxLevel;
			IsMax = isMax;
			CostType = costType;
			Cost = cost;
			Probability = probability;
			CanAfford = canAfford;
		}
	}

	// 제작 미리보기(표시용)
	public readonly struct CraftPreview
	{
		public readonly bool Valid;
		public readonly int ResultItemId;
		public readonly bool CanAfford;     // 재화+재료 모두 충분 여부

		public CraftPreview(bool valid, int resultItemId, bool canAfford)
		{
			Valid = valid;
			ResultItemId = resultItemId;
			CanAfford = canAfford;
		}
	}

	// 강화/제작 진입점 — 실행은 교체 가능한 Service(서버 대비), 표시용 조회/계산은 정적 메서드.
	public static class UpgradeSystem
	{
		static IUpgradeService _service = new LocalUpgradeService();

		public static IUpgradeService Service
		{
			get { return _service; }
		}

		// 다른 구현체(서버 등)로 교체 (부트 시점 1회 호출 가정)
		public static void SetService(IUpgradeService service)
		{
			if (service == null)
			{
				return;
			}

			_service = service;
		}

		// ── 실행 위임 ─────────────────────────────────────────────────

		public static UniTask<EnchantResponse> RequestEnchant(int itemId)
		{
			return _service.RequestEnchant(itemId);
		}

		public static UniTask<CraftResponse> RequestCraft(int craftId)
		{
			return _service.RequestCraft(craftId);
		}

		// ── 강화 조회(표시용) ─────────────────────────────────────────

		public static EnchantPreview GetEnchantPreview(int itemId)
		{
			Table_Equipment.Row equip = Table_Equipment.Get(itemId);
			if (equip == null || equip.EnchantInfo <= 0 || Account.Instance.Inventory.Has(itemId) == false)
			{
				return new EnchantPreview(false, 0, 0, false, CurrencyInfo.None, 0, 0f, false);
			}

			Table_Enchant.Row enchant = Table_Enchant.Get(equip.EnchantInfo);
			if (enchant == null)
			{
				return new EnchantPreview(false, 0, 0, false, CurrencyInfo.None, 0, 0f, false);
			}

			int current = Account.Instance.Inventory.GetEnhanceLevel(itemId);
			bool isMax = current >= enchant.MaxEnchantLv;
			int nextLevel = current + 1;
			int cost = CalcCost(enchant, nextLevel);
			float prob = CalcProbability(enchant, nextLevel);
			bool canAfford = isMax == false && CurrencyManager.Instance.GetAmount(enchant.CostType) >= cost;

			return new EnchantPreview(true, current, enchant.MaxEnchantLv, isMax, enchant.CostType, cost, prob, canAfford);
		}

		// ── 제작 조회(표시용) ─────────────────────────────────────────

		public static CraftPreview GetCraftPreview(int craftId)
		{
			Table_Craft.Row craft = Table_Craft.Get(craftId);
			if (craft == null)
			{
				return new CraftPreview(false, 0, false);
			}

			bool canAfford = HasCraftCost(craft) && HasCraftMaterials(craft);
			return new CraftPreview(true, craft.Result_EquipmentID, canAfford);
		}

		// ── 계산식 (단일 출처: 조회/실행 공용) ────────────────────────

		// 비용 = CostValue × 다음레벨 × CostLvModify
		public static int CalcCost(Table_Enchant.Row enchant, int nextLevel)
		{
			return Mathf.RoundToInt(enchant.CostValue * nextLevel * enchant.CostLvModify);
		}

		// 확률 = Probability + 다음레벨 × ProbabilityLvModify (0~1 클램프)
		public static float CalcProbability(Table_Enchant.Row enchant, int nextLevel)
		{
			return Mathf.Clamp01(enchant.Probability + nextLevel * enchant.ProbabilityLvModify);
		}

		public static bool HasCraftCost(Table_Craft.Row craft)
		{
			if (craft.CurrencyType_01 != CurrencyInfo.None && CurrencyManager.Instance.GetAmount(craft.CurrencyType_01) < craft.CurrencyCost_01)
			{
				return false;
			}

			if (craft.CurrencyType_02 != CurrencyInfo.None && CurrencyManager.Instance.GetAmount(craft.CurrencyType_02) < craft.CurrencyCost_02)
			{
				return false;
			}

			return true;
		}

		public static bool HasCraftMaterials(Table_Craft.Row craft)
		{
			if (craft.Material_01_ID > 0 && Account.Instance.Inventory.GetCount(craft.Material_01_ID) < craft.Material_01_Count)
			{
				return false;
			}

			if (craft.Material_02_ID > 0 && Account.Instance.Inventory.GetCount(craft.Material_02_ID) < craft.Material_02_Count)
			{
				return false;
			}

			if (craft.Material_03_ID > 0 && Account.Instance.Inventory.GetCount(craft.Material_03_ID) < craft.Material_03_Count)
			{
				return false;
			}

			return true;
		}
	}
}
