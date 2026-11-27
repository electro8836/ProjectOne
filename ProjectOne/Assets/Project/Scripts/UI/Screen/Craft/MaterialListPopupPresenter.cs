using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 획득처 종류 — 칩 색과 문구를 가른다.
	public enum MaterialSourceKind
	{
		Boss,		// 보스 처치 — 보스의 보상 그룹에 그 재료가 있다
		Combine,	// 조합 — 결과가 그 재료인 제작법이 있다
		Decompose,	// 분해 환급 — 티어 올리기 비용에 그 재료가 있다
	}

	// 획득처 한 줄.
	public struct MaterialSourceData
	{
		public MaterialSourceKind kind;
		public string text;
		public int recipeId;	// 조합만 — '조합하기' 바로가기 대상
	}

	// 보유 재료 1칸.
	public sealed class MaterialSlotData
	{
		public int itemId;
		public string name;
		public int owned;
		public readonly List<MaterialSourceData> sources = new List<MaterialSourceData>();
	}

	// 보유 재료 팝업 Presenter — 어떤 재료를 보이고 각 재료를 어디서 얻는지 테이블에서 찾는다.
	//
	// 대상: 제작법의 재료 칸(Mat1~4)이나 결과에 한 번이라도 나오는 Material 아이템(보물상자 열쇠처럼 제작과 무관한 재료는 빠진다).
	// 획득처는 문구 컬럼 없이 Monster·Reward·ForgeRecipe 테이블에서 찾는다.
	public sealed class MaterialListPopupPresenter : Presenter<MaterialListPopup>
	{
		private const int BOSS_NAME_LIMIT = 3;	// 이보다 많으면 "보스 N종" 으로 줄인다

		private readonly List<MaterialSlotData> _slots = new List<MaterialSlotData>();
		private readonly List<Table_ForgeRecipe.Row> _recipes = new List<Table_ForgeRecipe.Row>();
		private readonly List<ItemCost> _costBuffer = new List<ItemCost>(4);
		private readonly List<string> _bossNames = new List<string>();
		private readonly System.Text.StringBuilder _text = new System.Text.StringBuilder();
		private int _selected = -1;

		public async UniTask ShowAsync(CancellationToken ct)
		{
			build();

			await view.RenderAsync(_slots, ct);

			select(0);
			view.Reveal();
		}

		protected override void OnInitialize()
		{
			view.OnSlotClicked += onSlotClicked;
		}

		protected override void OnDispose()
		{
			view.OnSlotClicked -= onSlotClicked;
		}

		// ── 내부: 목록 ────────────────────────────────────────────────────

		private void build()
		{
			_slots.Clear();

			_recipes.Clear();
			Dictionary<int, Table_ForgeRecipe.Row>.Enumerator e = Table_ForgeRecipe.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				_recipes.Add(e.Current.Value);
			}

			_recipes.Sort(compareRecipeId);

			HashSet<int> ids = new HashSet<int>();
			for (int i = 0; i < _recipes.Count; i++)
			{
				Table_ForgeRecipe.Row recipe = _recipes[i];
				addIfMaterial(ids, recipe.ResultItemID);
				addIfMaterial(ids, recipe.Mat1_ID);
				addIfMaterial(ids, recipe.Mat2_ID);
				addIfMaterial(ids, recipe.Mat3_ID);
				addIfMaterial(ids, recipe.Mat4_ID);
			}

			List<int> sorted = new List<int>(ids);
			sorted.Sort();
			for (int i = 0; i < sorted.Count; i++)
			{
				MaterialSlotData slot = new MaterialSlotData();
				slot.itemId = sorted[i];
				slot.name = Table_Item.Get(slot.itemId).Name;
				slot.owned = Account.Instance.Inventory.GetCount(slot.itemId);
				addBossSource(slot);
				addCombineSource(slot);
				addDecomposeSource(slot);
				_slots.Add(slot);
			}
		}

		private static int compareRecipeId(Table_ForgeRecipe.Row a, Table_ForgeRecipe.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		private static void addIfMaterial(HashSet<int> ids, int itemId)
		{
			if (itemId == 0)
			{
				return;
			}

			Table_Item.Row item = Table_Item.Get(itemId);
			if (item != null && item.MainCategory == ItemMainCategory.Material)
			{
				ids.Add(itemId);
			}
		}

		// ── 내부: 획득처 ──────────────────────────────────────────────────

		// 보스 처치 — 보상 그룹에 그 재료가 있는 보스 이름을 나열한다.
		private void addBossSource(MaterialSlotData slot)
		{
			HashSet<int> groups = new HashSet<int>();
			string target = slot.itemId.ToString();
			Dictionary<int, Table_Reward.Row>.Enumerator rewards = Table_Reward.All().GetEnumerator();
			while (rewards.MoveNext() == true)
			{
				Table_Reward.Row reward = rewards.Current.Value;
				if (reward.RewardType == RewardType.Item && reward.TargetID == target)
				{
					groups.Add(reward.GroupID);
				}
			}

			_bossNames.Clear();
			Dictionary<int, Table_Monster.Row>.Enumerator monsters = Table_Monster.All().GetEnumerator();
			while (monsters.MoveNext() == true)
			{
				Table_Monster.Row monster = monsters.Current.Value;
				if (monster.MonsterType == MonsterType.Boss && groups.Contains(monster.RewardGroupID) == true && _bossNames.Contains(monster.Name) == false)
				{
					_bossNames.Add(monster.Name);
				}
			}

			if (_bossNames.Count == 0)
			{
				return;
			}

			MaterialSourceData source = new MaterialSourceData();
			source.kind = MaterialSourceKind.Boss;
			source.text = (_bossNames.Count > BOSS_NAME_LIMIT) ? "보스 " + _bossNames.Count + "종" : string.Join(", ", _bossNames);
			slot.sources.Add(source);
		}

		// 조합 — 결과가 그 재료인 제작법마다 한 줄. 문구는 재료 목록.
		private void addCombineSource(MaterialSlotData slot)
		{
			for (int r = 0; r < _recipes.Count; r++)
			{
				Table_ForgeRecipe.Row recipe = _recipes[r];
				if (recipe.ResultItemID != slot.itemId)
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
				slot.sources.Add(source);
			}
		}

		// 분해 환급 — 티어 올리기 비용에 그 재료가 있으면, 그 제작법 결과 중 가장 낮은 티어부터 돌려받는다.
		private void addDecomposeSource(MaterialSlotData slot)
		{
			int minTier = int.MaxValue;
			for (int i = 0; i < _recipes.Count; i++)
			{
				Table_ForgeRecipe.Row recipe = _recipes[i];
				if (ForgeRules.GetKind(recipe) != ForgeRecipeKind.Upgrade || usesItem(recipe, slot.itemId) == false)
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
			source.text = minTier + "티어 이상 장비 분해";
			slot.sources.Add(source);
		}

		private static bool usesItem(Table_ForgeRecipe.Row recipe, int itemId)
		{
			return recipe.Mat1_ID == itemId || recipe.Mat2_ID == itemId || recipe.Mat3_ID == itemId || recipe.Mat4_ID == itemId;
		}

		// ── 내부: 선택 ────────────────────────────────────────────────────

		private void select(int index)
		{
			if (index < 0 || index >= _slots.Count)
			{
				return;
			}

			_selected = index;
			view.ShowDetail(_slots[index], index);
		}

		private void onSlotClicked(int index)
		{
			if (index == _selected)
			{
				return;
			}

			select(index);
		}
	}
}
