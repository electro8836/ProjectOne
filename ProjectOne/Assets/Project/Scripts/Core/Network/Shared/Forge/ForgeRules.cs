using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 재료 비용 1건 — 스택 아이템과 수량.
	public struct ItemCost
	{
		public int itemId;
		public int count;
	}

	// 조합법 종류 — 테이블 컬럼이 아니라 행 내용으로 정한다.
	public enum ForgeRecipeKind
	{
		Invalid,
		Material,	// 재료 → 상위 재료 (항상 성공, 묶음 제작 가능)
		Base,		// 재료 + 재화 → 1티어 기초 장비
		Upgrade,	// 이전 티어 장비 + 재료 + 재화 → 다음 티어 장비 (같은 UID)
	}

	// 제작이 막힌 이유. UI 가 버튼 상태와 문구를 이걸로 정한다.
	public enum ForgeBlock
	{
		None,
		Invalid,			// 조합법이 없거나 잘못된 행
		NoSource,			// 티어 올리기인데 이전 장비가 지정되지 않음
		SourceMismatch,		// 지정한 장비가 조합법의 이전 장비와 다름
		NotEnoughItems,		// 재료 부족
		NotEnoughCurrency,	// 재화 부족 — 보유량 판정은 호출자가 한다
		InventoryFull,		// 기초 제작인데 인벤토리 빈칸이 없음
	}

	// 재련이 막힌 이유.
	public enum ReforgeBlock
	{
		None,
		Invalid,		// 장비·재련 행이 없음
		MaxQuality,		// 이미 품질 최대
	}

	// 제작·재련 규칙 (제작시스템 설계 3·4장). 클라·서버 공용.
	//
	// 판정·비용은 결정적이라 양쪽이 같다. 확률(제작 성공·재련 상승량) 굴림은 서버만 한다.
	public static class ForgeRules
	{
		public const int RateMax = 1000;	// 확률 단위 ‰

		// 재료 조합 1회 요청의 최대 묶음 수
		public const int MaxMaterialCount = 100;

		// 티어 올리기 조합법을 이전 장비로 찾는 인덱스 (갈래가 있어 여러 개)
		private static Dictionary<int, List<Table_ForgeRecipe.Row>> _bySource;
		private static Dictionary<ItemGradeType, Table_ForgeReforge.Row> _reforgeByGrade;

		// ── 티어 ─────────────────────────────────────────────────────────

		// 장비의 티어. 장비가 아니면 0, 값이 비었으면 1.
		public static int GetTier(int itemId)
		{
			Table_Equipment.Row row = Table_Equipment.Get(itemId);
			if (row == null)
			{
				return 0;
			}

			return (row.Tier > 0) ? row.Tier : 1;
		}

		// ── 조합법 ────────────────────────────────────────────────────

		public static ForgeRecipeKind GetKind(Table_ForgeRecipe.Row recipe)
		{
			if (recipe == null || recipe.ResultItemID <= 0)
			{
				return ForgeRecipeKind.Invalid;
			}

			bool resultIsEquipment = Table_Equipment.Get(recipe.ResultItemID) != null;
			if (recipe.SourceItemID > 0)
			{
				return resultIsEquipment ? ForgeRecipeKind.Upgrade : ForgeRecipeKind.Invalid;
			}

			return resultIsEquipment ? ForgeRecipeKind.Base : ForgeRecipeKind.Material;
		}

		// 티어 올리기 조합법이 사슬 규칙을 지키는지 — 같은 부위·무기 종류, 티어 +1.
		public static bool IsValidUpgrade(Table_ForgeRecipe.Row recipe)
		{
			Table_Equipment.Row source = Table_Equipment.Get(recipe.SourceItemID);
			Table_Equipment.Row result = Table_Equipment.Get(recipe.ResultItemID);
			if (source == null || result == null)
			{
				return false;
			}

			if (source.EquipSlotType != result.EquipSlotType || source.WeaponType != result.WeaponType)
			{
				return false;
			}

			return GetTier(recipe.ResultItemID) == GetTier(recipe.SourceItemID) + 1;
		}

		// 이 장비에서 이어지는 티어 올리기 조합법(갈래)을 buffer 에 채운다.
		public static void GetUpgradeRecipes(int sourceItemId, List<Table_ForgeRecipe.Row> buffer)
		{
			buffer.Clear();
			ensureIndex();

			List<Table_ForgeRecipe.Row> list;
			if (_bySource.TryGetValue(sourceItemId, out list) == true)
			{
				for (int i = 0; i < list.Count; i++)
				{
					buffer.Add(list[i]);
				}
			}
		}

		// 이 결과를 만드는 조합법(첫 행). 트리에서 하위 재료의 조합법을 찾을 때 쓴다. 없으면 null.
		public static Table_ForgeRecipe.Row FindRecipeByResult(int resultItemId)
		{
			Dictionary<int, Table_ForgeRecipe.Row>.Enumerator e = Table_ForgeRecipe.All().GetEnumerator();
			Table_ForgeRecipe.Row found = null;
			while (e.MoveNext() == true)
			{
				Table_ForgeRecipe.Row row = e.Current.Value;
				if (row.ResultItemID == resultItemId && (found == null || row.ID < found.ID))
				{
					found = row;
				}
			}

			return found;
		}

		// count 회분 재료를 buffer 에 채운다.
		public static void GetItemCosts(Table_ForgeRecipe.Row recipe, int count, List<ItemCost> buffer)
		{
			buffer.Clear();
			addItemCost(buffer, recipe.Mat1_ID, recipe.Mat1_Count, count);
			addItemCost(buffer, recipe.Mat2_ID, recipe.Mat2_Count, count);
			addItemCost(buffer, recipe.Mat3_ID, recipe.Mat3_Count, count);
			addItemCost(buffer, recipe.Mat4_ID, recipe.Mat4_Count, count);
		}

		// count 회분 재화를 buffer 에 채운다.
		public static void GetCurrencyCosts(Table_ForgeRecipe.Row recipe, int count, List<CurrencyCost> buffer)
		{
			buffer.Clear();
			addCurrencyCost(buffer, recipe.Cur1_ID, recipe.Cur1_Count, count);
			addCurrencyCost(buffer, recipe.Cur2_ID, recipe.Cur2_Count, count);
		}

		// 성공 확률(‰). 실패가 쌓일 때마다 FailBonus 씩 오르고 1000 에서 멈춘다. 재료 조합·기초 제작은 항상 1000.
		public static int GetSuccessRate(Table_ForgeRecipe.Row recipe, int failCount)
		{
			if (GetKind(recipe) != ForgeRecipeKind.Upgrade)
			{
				return RateMax;
			}

			int rate = recipe.SuccessRate + failCount * recipe.FailBonus;
			if (rate > RateMax)
			{
				return RateMax;
			}

			return (rate < 0) ? 0 : rate;
		}

		// 성공 확률 굴림 — 서버 전용.
		public static bool RollSuccess(int rate, IRandomSource rng)
		{
			if (rate >= RateMax)
			{
				return true;
			}

			return rng.Range(0, RateMax) < rate;
		}

		// ── 재련 ──────────────────────────────────────────────────────

		public static Table_ForgeReforge.Row GetReforgeRow(ItemGradeType grade)
		{
			if (_reforgeByGrade == null)
			{
				_reforgeByGrade = new Dictionary<ItemGradeType, Table_ForgeReforge.Row>();
				Dictionary<int, Table_ForgeReforge.Row>.Enumerator e = Table_ForgeReforge.All().GetEnumerator();
				while (e.MoveNext() == true)
				{
					_reforgeByGrade[e.Current.Value.Grade] = e.Current.Value;
				}
			}

			Table_ForgeReforge.Row row;
			_reforgeByGrade.TryGetValue(grade, out row);
			return row;
		}

		public static ReforgeBlock GetReforgeBlock(int itemId, ItemGradeType grade, int quality)
		{
			if (Table_Equipment.Get(itemId) == null || GetReforgeRow(grade) == null)
			{
				return ReforgeBlock.Invalid;
			}

			if (quality >= EquipmentQuality.Max)
			{
				return ReforgeBlock.MaxQuality;
			}

			return ReforgeBlock.None;
		}

		public static void GetReforgeCosts(ItemGradeType grade, List<CurrencyCost> buffer)
		{
			buffer.Clear();
			Table_ForgeReforge.Row row = GetReforgeRow(grade);
			if (row == null)
			{
				return;
			}

			addCurrencyCost(buffer, row.Cur1_ID, row.Cur1_Count, 1);
			addCurrencyCost(buffer, row.Cur2_ID, row.Cur2_Count, 1);
		}

		// 상승량 범위(0.1% 단위). 표시용으로도 쓴다.
		public static void GetReforgeRise(ItemGradeType grade, out int min, out int max)
		{
			min = 0;
			max = 0;
			Table_ForgeReforge.Row row = GetReforgeRow(grade);
			if (row == null)
			{
				return;
			}

			min = EquipmentQuality.FromPercent(row.MinRise);
			max = EquipmentQuality.FromPercent(row.MaxRise);
			if (max < min)
			{
				max = min;
			}
		}

		// 상승량을 굴려 더한 새 품질 — 서버 전용. 100% 를 넘으면 100% 로 맞춘다.
		public static int RollReforge(ItemGradeType grade, int quality, IRandomSource rng)
		{
			int min;
			int max;
			GetReforgeRise(grade, out min, out max);

			int next = quality + rng.Range(min, max + 1);
			return (next > EquipmentQuality.Max) ? EquipmentQuality.Max : next;
		}

		// ── 분해 환급 ─────────────────────────────────────────────────
		//
		// 티어 올리기 제작비의 일부를 돌려준다 (제작시스템 설계 7장). 쓴 내역을 저장하지 않고 테이블로 계산한다.
		//   그 장비를 결과로 내는 티어 올리기 조합법을 이전 장비 쪽으로 거슬러 올라가며 재료·재화를 합하고,
		//   합계 × DecomposeRefundRate 를 버림한다. 1티어 기초 제작비·재련비·실패로 잃은 재료는 돌려주지 않는다.

		// 환급분을 items·currencies 에 더한다 (같은 ID 는 합친다). 비우지 않으므로 일괄 분해 합계에 바로 쓴다.
		public static void AddDecomposeRefund(int itemId, List<ItemCost> items, List<CurrencyCost> currencies)
		{
			List<ItemCost> chainItems = null;
			List<CurrencyCost> chainCurrencies = null;

			// 사슬 길이는 티어 단계를 넘지 않는다 — 잘못된 데이터로 순환해도 멈추도록 횟수를 묶는다.
			int current = itemId;
			int steps = GetTier(itemId);
			for (int i = 0; i < steps; i++)
			{
				Table_ForgeRecipe.Row recipe = FindRecipeByResult(current);
				if (recipe == null || GetKind(recipe) != ForgeRecipeKind.Upgrade)
				{
					break;
				}

				if (chainItems == null)
				{
					chainItems = new List<ItemCost>(4);
					chainCurrencies = new List<CurrencyCost>(2);
				}

				mergeItem(chainItems, recipe.Mat1_ID, recipe.Mat1_Count);
				mergeItem(chainItems, recipe.Mat2_ID, recipe.Mat2_Count);
				mergeItem(chainItems, recipe.Mat3_ID, recipe.Mat3_Count);
				mergeItem(chainItems, recipe.Mat4_ID, recipe.Mat4_Count);
				mergeCurrency(chainCurrencies, recipe.Cur1_ID, recipe.Cur1_Count);
				mergeCurrency(chainCurrencies, recipe.Cur2_ID, recipe.Cur2_Count);

				current = recipe.SourceItemID;
			}

			if (chainItems == null)
			{
				return;
			}

			for (int i = 0; i < chainItems.Count; i++)
			{
				mergeItem(items, chainItems[i].itemId, (int)(chainItems[i].count * EquipmentGrowthRules.DecomposeRefundRate));
			}

			for (int i = 0; i < chainCurrencies.Count; i++)
			{
				mergeCurrency(currencies, chainCurrencies[i].currency, (int)(chainCurrencies[i].amount * EquipmentGrowthRules.DecomposeRefundRate));
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void mergeItem(List<ItemCost> buffer, int itemId, int count)
		{
			if (itemId <= 0 || count <= 0)
			{
				return;
			}

			for (int i = 0; i < buffer.Count; i++)
			{
				if (buffer[i].itemId == itemId)
				{
					ItemCost sum = buffer[i];
					sum.count += count;
					buffer[i] = sum;
					return;
				}
			}

			ItemCost cost;
			cost.itemId = itemId;
			cost.count = count;
			buffer.Add(cost);
		}

		private static void mergeCurrency(List<CurrencyCost> buffer, EDT.Currency currency, int amount)
		{
			if (currency == EDT.Currency.None || amount <= 0)
			{
				return;
			}

			for (int i = 0; i < buffer.Count; i++)
			{
				if (buffer[i].currency == currency)
				{
					CurrencyCost sum = buffer[i];
					sum.amount += amount;
					buffer[i] = sum;
					return;
				}
			}

			CurrencyCost cost;
			cost.currency = currency;
			cost.amount = amount;
			buffer.Add(cost);
		}

		private static void ensureIndex()
		{
			if (_bySource != null)
			{
				return;
			}

			_bySource = new Dictionary<int, List<Table_ForgeRecipe.Row>>();
			Dictionary<int, Table_ForgeRecipe.Row>.Enumerator e = Table_ForgeRecipe.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ForgeRecipe.Row row = e.Current.Value;
				if (row.SourceItemID <= 0)
				{
					continue;
				}

				List<Table_ForgeRecipe.Row> list;
				if (_bySource.TryGetValue(row.SourceItemID, out list) == false)
				{
					list = new List<Table_ForgeRecipe.Row>(2);
					_bySource.Add(row.SourceItemID, list);
				}

				list.Add(row);
			}
		}

		private static void addItemCost(List<ItemCost> buffer, int itemId, int amount, int count)
		{
			if (itemId <= 0 || amount <= 0)
			{
				return;
			}

			ItemCost cost;
			cost.itemId = itemId;
			cost.count = amount * count;
			buffer.Add(cost);
		}

		private static void addCurrencyCost(List<CurrencyCost> buffer, EDT.Currency currency, int amount, int count)
		{
			if (currency == EDT.Currency.None || amount <= 0)
			{
				return;
			}

			CurrencyCost cost;
			cost.currency = currency;
			cost.amount = amount * count;
			buffer.Add(cost);
		}
	}
}
