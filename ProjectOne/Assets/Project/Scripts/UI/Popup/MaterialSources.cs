using System.Collections.Generic;
using EDT;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 획득처 종류 — 칩 색과 문구를 가른다.
	public enum MaterialSourceKind
	{
		Boss,		// 보스 처치 — 보스의 보상 그룹에 그 재료가 있다
		Combine,	// 조합 — 결과가 그 재료인 제작법이 있다
		Decompose,	// 분해 환급 — 티어 올리기 비용에 그 재료가 있다
	}

	// 획득처 한 줄. 처치는 보스마다 한 줄이다(문구 = 보스 이름).
	public struct MaterialSourceData
	{
		public MaterialSourceKind kind;
		public string text;
		public int recipeId;	// 조합만 — '조합하기' 바로가기 대상
	}

	// 사용처 한 줄 — 이 재료가 재료 칸(Mat1~4)에 직접 들어가는 제작법.
	public struct MaterialUseData
	{
		public int recipeId;
		public int resultItemId;
		public int count;	// 제작 1회에 드는 수
	}

	// 재료 획득처·사용처 — 문구 컬럼 없이 Monster·Reward·ForgeRecipe 테이블에서 찾는다.
	// 재료(Material)가 아니면 아무것도 찾지 않는다.
	// 사용처는 직접 들어가는 제작법만 센다 — 그 결과로 다시 만드는 상위 장비까지 거슬러 올라가지 않는다.
	public static class MaterialSources
	{
		private static readonly List<Table_ForgeRecipe.Row> _recipes = new List<Table_ForgeRecipe.Row>();
		private static readonly List<ItemCost> _costBuffer = new List<ItemCost>(4);
		private static readonly List<string> _bossNames = new List<string>();
		private static readonly HashSet<int> _groups = new HashSet<int>();
		private static readonly System.Text.StringBuilder _text = new System.Text.StringBuilder();

		// 획득처를 buffer 에 채운다(먼저 비운다). 순서는 처치 → 조합 → 분해.
		public static void Collect(int itemId, List<MaterialSourceData> buffer)
		{
			buffer.Clear();

			Table_Item.Row item = Table_Item.Get(itemId);
			if (item == null || item.MainCategory != ItemMainCategory.Material)
			{
				return;
			}

			loadRecipes();
			addBossSource(itemId, buffer);
			addCombineSource(itemId, buffer);
			addDecomposeSource(itemId, buffer);
		}

		// 사용처를 buffer 에 채운다(먼저 비운다). 순서는 재료 조합 → 낮은 티어 → 제작법 ID.
		public static void CollectUses(int itemId, List<MaterialUseData> buffer)
		{
			buffer.Clear();

			Table_Item.Row item = Table_Item.Get(itemId);
			if (item == null || item.MainCategory != ItemMainCategory.Material)
			{
				return;
			}

			loadRecipes();
			_recipes.Sort(compareUse);
			for (int i = 0; i < _recipes.Count; i++)
			{
				Table_ForgeRecipe.Row recipe = _recipes[i];
				int count = getMatCount(recipe, itemId);
				if (count <= 0)
				{
					continue;
				}

				MaterialUseData use = new MaterialUseData();
				use.recipeId = recipe.ID;
				use.resultItemId = recipe.ResultItemID;
				use.count = count;
				buffer.Add(use);
			}
		}

		private static void loadRecipes()
		{
			_recipes.Clear();
			Dictionary<int, Table_ForgeRecipe.Row>.Enumerator e = Table_ForgeRecipe.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				_recipes.Add(e.Current.Value);
			}

			_recipes.Sort(compareRecipeId);
		}

		// 재료 조합이 먼저, 그다음 결과 티어 오름차순, 같으면 제작법 ID.
		private static int compareUse(Table_ForgeRecipe.Row a, Table_ForgeRecipe.Row b)
		{
			int ka = (ForgeRules.GetKind(a) == ForgeRecipeKind.Material) ? 0 : 1;
			int kb = (ForgeRules.GetKind(b) == ForgeRecipeKind.Material) ? 0 : 1;
			if (ka != kb)
			{
				return ka.CompareTo(kb);
			}

			int ta = ForgeRules.GetTier(a.ResultItemID);
			int tb = ForgeRules.GetTier(b.ResultItemID);
			if (ta != tb)
			{
				return ta.CompareTo(tb);
			}

			return a.ID.CompareTo(b.ID);
		}

		private static int compareRecipeId(Table_ForgeRecipe.Row a, Table_ForgeRecipe.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		// 처치 — 보상 그룹에 그 재료가 있는 보스마다 한 줄.
		private static void addBossSource(int itemId, List<MaterialSourceData> buffer)
		{
			_groups.Clear();
			string target = itemId.ToString();
			Dictionary<int, Table_Reward.Row>.Enumerator rewards = Table_Reward.All().GetEnumerator();
			while (rewards.MoveNext() == true)
			{
				Table_Reward.Row reward = rewards.Current.Value;
				if (reward.RewardType == RewardType.Item && reward.TargetID == target)
				{
					_groups.Add(reward.GroupID);
				}
			}

			_bossNames.Clear();
			Dictionary<int, Table_Monster.Row>.Enumerator monsters = Table_Monster.All().GetEnumerator();
			while (monsters.MoveNext() == true)
			{
				Table_Monster.Row monster = monsters.Current.Value;
				if (monster.MonsterType == MonsterType.Boss && _groups.Contains(monster.RewardGroupID) == true && _bossNames.Contains(monster.Name) == false)
				{
					_bossNames.Add(monster.Name);
				}
			}

			for (int i = 0; i < _bossNames.Count; i++)
			{
				MaterialSourceData source = new MaterialSourceData();
				source.kind = MaterialSourceKind.Boss;
				source.text = _bossNames[i];
				buffer.Add(source);
			}
		}

		// 조합 — 결과가 그 재료인 제작법마다 한 줄. 문구는 재료 목록.
		private static void addCombineSource(int itemId, List<MaterialSourceData> buffer)
		{
			for (int r = 0; r < _recipes.Count; r++)
			{
				Table_ForgeRecipe.Row recipe = _recipes[r];
				if (recipe.ResultItemID != itemId)
				{
					continue;
				}

				ForgeRules.GetItemCosts(recipe, 1, _costBuffer);
				_text.Clear();
				for (int i = 0; i < _costBuffer.Count; i++)
				{
					if (i > 0)
					{
						_text.Append(" + ");
					}

					Table_Item.Row mat = Table_Item.Get(_costBuffer[i].itemId);
					_text.Append((mat != null) ? mat.Name : _costBuffer[i].itemId.ToString());
					_text.Append(" x");
					_text.Append(_costBuffer[i].count);
				}

				MaterialSourceData source = new MaterialSourceData();
				source.kind = MaterialSourceKind.Combine;
				source.text = _text.ToString();
				source.recipeId = recipe.ID;
				buffer.Add(source);
			}
		}

		// 분해 환급 — 티어 올리기 비용에 그 재료가 있으면, 그 제작법 결과 중 가장 낮은 티어부터 돌려받는다.
		private static void addDecomposeSource(int itemId, List<MaterialSourceData> buffer)
		{
			int minTier = int.MaxValue;
			for (int i = 0; i < _recipes.Count; i++)
			{
				Table_ForgeRecipe.Row recipe = _recipes[i];
				if (ForgeRules.GetKind(recipe) != ForgeRecipeKind.Upgrade || usesItem(recipe, itemId) == false)
				{
					continue;
				}

				int tier = ForgeRules.GetTier(recipe.ResultItemID);
				if (tier < minTier)
				{
					minTier = tier;
				}
			}

			if (minTier == int.MaxValue)
			{
				return;
			}

			MaterialSourceData source = new MaterialSourceData();
			source.kind = MaterialSourceKind.Decompose;
			source.text = minTier + "티어 이상 장비";
			buffer.Add(source);
		}

		private static bool usesItem(Table_ForgeRecipe.Row recipe, int itemId)
		{
			return getMatCount(recipe, itemId) > 0;
		}

		// 재료 칸(Mat1~4)에서 그 재료의 필요 수. 없으면 0.
		private static int getMatCount(Table_ForgeRecipe.Row recipe, int itemId)
		{
			if (recipe.Mat1_ID == itemId)
			{
				return recipe.Mat1_Count;
			}

			if (recipe.Mat2_ID == itemId)
			{
				return recipe.Mat2_Count;
			}

			if (recipe.Mat3_ID == itemId)
			{
				return recipe.Mat3_Count;
			}

			if (recipe.Mat4_ID == itemId)
			{
				return recipe.Mat4_Count;
			}

			return 0;
		}
	}
}
