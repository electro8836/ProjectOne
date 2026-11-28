using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Forge;
using ProjectOne.Items;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.Upgrade;
using ProjectOne.UserData;
using UnityEngine;

namespace ProjectOne.UI
{
	// 조합법 행 상태 — 목록 오른쪽 알약.
	public enum RecipeState
	{
		Craftable,	// 제작 가능
		Short,		// 재료·재화 부족 (또는 가방 가득)
		Locked,		// 티어 올리기인데 이전 장비가 없음
	}

	// 조합법 목록 1행 렌더 데이터. 재료 그림은 공용 목록(RecipeCostIcon)의 [costStart, costStart + costCount) 구간이다.
	public struct RecipeRowData
	{
		public int recipeId;
		public int resultItemId;
		public ItemGradeType grade;
		public string name;
		public RecipeState state;
		public int costStart;
		public int costCount;
	}

	// 조합법 행의 재료 그림 1칸 — 트리 칸과 같은 순서(이전 장비 · 재료 · 재화). ready 면 다 갖춘 칸.
	public struct RecipeCostIcon
	{
		public bool isCurrency;
		public int itemId;
		public EDT.Currency currency;
		public bool ready;
	}

	// 조합법 트리 렌더 데이터 — 결과 · (티어 올리기면) 이전 장비 · 재료 · 재화 · 버튼.
	public sealed class ForgeStageData
	{
		public int resultItemId;
		public ItemGradeType resultGrade;
		public int resultCount;
		public string resultName;

		public bool isUpgrade;
		public EquipmentInstance source;	// 보유한 이전 장비(없으면 null)
		public int sourceItemId;
		public string sourceName;

		public readonly List<ForgeItemCost> items = new List<ForgeItemCost>(4);
		public readonly List<UpgradeCost> currencies = new List<UpgradeCost>(2);

		public bool canCraft;
		public string rateText;
		public string hint;
	}

	// 제작 모드 — 조합법 목록·트리·제작 요청 (제작시스템 설계 3장).
	public sealed partial class CraftPresenter
	{
		// 조합법 분류 탭 — CraftScreen.uxml 의 recipe-tab-N 순서
		private const int RECIPE_TAB_MATERIAL = 0;
		private const int RECIPE_TAB_WEAPON = 1;
		private const int RECIPE_TAB_ARMOR = 2;
		private const int RECIPE_TAB_ACCESSORY = 3;
		private const int RECIPE_TAB_RELIC = 4;

		// 분류 탭 아래 세부 선택(분할 막대). 재료·유물은 세부 선택이 없다. 방어구·장신구의 0번은 전체.
		private static readonly string[] SUBS_NONE = new string[0];
		private static readonly string[] SUBS_WEAPON = { "대검", "단검", "수리검", "소총" };
		private static readonly WeaponType[] SUB_WEAPON_TYPES = { WeaponType.GreatSword, WeaponType.Dagger, WeaponType.Shuriken, WeaponType.Rifle };
		private static readonly string[] SUBS_ARMOR = { "전체", "투구", "갑옷", "장갑", "신발" };
		private static readonly EquipSlotTypes[] SUB_ARMOR_SLOTS = { EquipSlotTypes.None, EquipSlotTypes.Helmet, EquipSlotTypes.Armor, EquipSlotTypes.Gloves, EquipSlotTypes.Boots };
		private static readonly string[] SUBS_ACCESSORY = { "전체", "반지", "목걸이" };
		private static readonly EquipSlotTypes[] SUB_ACCESSORY_SLOTS = { EquipSlotTypes.None, EquipSlotTypes.Ring, EquipSlotTypes.Amulet };

		private const string HINT_UPGRADE = "실패 시 재료만 소모 · 실패할수록 확률 상승";
		private const string HINT_BASE = "품질은 무작위";
		private const string RATE_PREFIX = "확률 ";

		private int _recipeTab = RECIPE_TAB_MATERIAL;
		private int _recipeSub;
		private int _selectedRecipeId;

		private readonly List<Table_ForgeRecipe.Row> _recipes = new List<Table_ForgeRecipe.Row>();
		private readonly List<RecipeRowData> _recipeRows = new List<RecipeRowData>();
		private readonly ForgeStageData _stage = new ForgeStageData();
		private readonly List<ForgeItemCost> _itemCostBuffer = new List<ForgeItemCost>(4);
		private readonly List<UpgradeCost> _currencyCostBuffer = new List<UpgradeCost>(2);
		private readonly List<RecipeCostIcon> _rowCosts = new List<RecipeCostIcon>();

		private void initializeForge()
		{
			view.OnRecipeTabSelected += onRecipeTabSelected;
			view.OnRecipeSubSelected += onRecipeSubSelected;
			view.OnRecipeSelected += onRecipeSelected;
			view.OnForgeActionClicked += onForgeActionClicked;
			view.OnForgeCellClicked += onForgeCellClicked;
			view.OnMaterialListClicked += onMaterialListClicked;
		}

		private void disposeForge()
		{
			view.OnRecipeTabSelected -= onRecipeTabSelected;
			view.OnRecipeSubSelected -= onRecipeSubSelected;
			view.OnRecipeSelected -= onRecipeSelected;
			view.OnForgeActionClicked -= onForgeActionClicked;
			view.OnForgeCellClicked -= onForgeCellClicked;
			view.OnMaterialListClicked -= onMaterialListClicked;
		}

		// ── 입력 ──────────────────────────────────────────────────────

		private void onRecipeTabSelected(int index)
		{
			_recipeTab = index;
			_recipeSub = getDefaultSub(index);
			_selectedRecipeId = 0;
			view.SetRecipeSubs(getSubLabels(_recipeTab), _recipeSub);
			rebuild();
		}

		private void onRecipeSubSelected(int index)
		{
			_recipeSub = index;
			_selectedRecipeId = 0;
			rebuild();
		}

		private void onRecipeSelected(int recipeId)
		{
			_selectedRecipeId = recipeId;
			rebuild();
		}

		// 보유 재료 팝업 — '조합하기'나 사용처 줄로 닫히면 그 제작법으로 넘어간다.
		private void onMaterialListClicked()
		{
			showMaterialListAsync().Forget();
		}

		private async UniTaskVoid showMaterialListAsync()
		{
			int recipeId = await UIManager.Instance.ShowMaterialListPopupAsync(view.GetDestroyToken());
			if (recipeId != 0)
			{
				selectRecipe(recipeId);
			}
		}

		// 그 제작법이 있는 탭·세부 선택(재료 / 무기 종류 / 방어구·장신구 부위 / 유물)으로 바꾸고 그 행을 고른다.
		private void selectRecipe(int recipeId)
		{
			Table_ForgeRecipe.Row recipe = Table_ForgeRecipe.Get(recipeId);
			_recipeTab = getRecipeTab(recipe);
			_recipeSub = getRecipeSub(recipe, _recipeTab);
			_selectedRecipeId = recipeId;
			applyRecipeTabView();
			rebuild();
		}

		// 분류 탭·세부 선택 표시를 지금 값으로 맞춘다.
		private void applyRecipeTabView()
		{
			view.SelectRecipeTab(_recipeTab);
			view.SetRecipeSubs(getSubLabels(_recipeTab), _recipeSub);
		}

		private static string[] getSubLabels(int tab)
		{
			switch (tab)
			{
				case RECIPE_TAB_WEAPON:
					return SUBS_WEAPON;

				case RECIPE_TAB_ARMOR:
					return SUBS_ARMOR;

				case RECIPE_TAB_ACCESSORY:
					return SUBS_ACCESSORY;
			}

			return SUBS_NONE;
		}

		// 탭을 눌렀을 때 먼저 골라지는 세부 선택. 무기 탭은 장착 무기 종류(미착용이면 첫 칸), 나머지는 0.
		private static int getDefaultSub(int tab)
		{
			if (tab != RECIPE_TAB_WEAPON)
			{
				return 0;
			}

			WeaponType weaponType = Account.Instance.Loadout.EquippedWeaponType;
			return Mathf.Max(0, System.Array.IndexOf(SUB_WEAPON_TYPES, weaponType));
		}

		// 제작법이 들어가는 분류 탭. 장비 제작법은 결과 장비의 무기 종류·부위로 가른다.
		private static int getRecipeTab(Table_ForgeRecipe.Row recipe)
		{
			if (recipe == null || ForgeRules.GetKind(recipe) == ForgeRecipeKind.Material)
			{
				return RECIPE_TAB_MATERIAL;
			}

			Table_Equipment.Row equipment = Table_Equipment.Get(recipe.ResultItemID);
			if (equipment == null)
			{
				return RECIPE_TAB_MATERIAL;
			}

			if (equipment.WeaponType != WeaponType.None)
			{
				return RECIPE_TAB_WEAPON;
			}

			switch (equipment.EquipSlotType)
			{
				case EquipSlotTypes.Helmet:
				case EquipSlotTypes.Armor:
				case EquipSlotTypes.Gloves:
				case EquipSlotTypes.Boots:
					return RECIPE_TAB_ARMOR;

				case EquipSlotTypes.Ring:
				case EquipSlotTypes.Amulet:
					return RECIPE_TAB_ACCESSORY;

				case EquipSlotTypes.Relic:
					return RECIPE_TAB_RELIC;
			}

			return RECIPE_TAB_MATERIAL;
		}

		// 그 탭 안에서 제작법이 들어가는 세부 선택 칸. 세부 선택이 없거나 못 찾으면 0.
		private static int getRecipeSub(Table_ForgeRecipe.Row recipe, int tab)
		{
			Table_Equipment.Row equipment = (recipe != null) ? Table_Equipment.Get(recipe.ResultItemID) : null;
			if (equipment == null)
			{
				return 0;
			}

			switch (tab)
			{
				case RECIPE_TAB_WEAPON:
					return Mathf.Max(0, System.Array.IndexOf(SUB_WEAPON_TYPES, equipment.WeaponType));

				case RECIPE_TAB_ARMOR:
					return Mathf.Max(0, System.Array.IndexOf(SUB_ARMOR_SLOTS, equipment.EquipSlotType));

				case RECIPE_TAB_ACCESSORY:
					return Mathf.Max(0, System.Array.IndexOf(SUB_ACCESSORY_SLOTS, equipment.EquipSlotType));
			}

			return 0;
		}

		// 트리 칸 클릭 — 장비는 읽기 전용 장비 정보, 재료는 아이템 정보(설명·획득처), 재화는 툴팁.
		private void onForgeCellClicked(ItemCellView cell)
		{
			object tag = cell.Tag;
			if (tag is EDT.Currency)
			{
				showCurrencyTooltip((EDT.Currency)tag, cell);
				return;
			}

			string key = tag as string;
			if (key == CraftUI.FORGE_TAG_SOURCE)
			{
				if (_stage.source != null)
				{
					UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, _stage.source, view.GetDestroyToken()).Forget();
					return;
				}

				showItemInfo(_stage.sourceItemId, ItemGradeType.None);
				return;
			}

			if (key == CraftUI.FORGE_TAG_RESULT)
			{
				showItemInfo(_stage.resultItemId, _stage.resultGrade);
				return;
			}

			if (tag is int)
			{
				showItemInfo((int)tag, ItemGradeType.None);
			}
		}

		// 장비면 표시용 인스턴스로 읽기 전용 장비 정보를, 그 밖의 아이템은 읽기 전용 아이템 정보(획득처 포함)를 띄운다.
		private void showItemInfo(int itemId, ItemGradeType grade)
		{
			if (Table_Equipment.Get(itemId) != null)
			{
				EquipmentInstance preview = buildPreview(itemId, grade);
				if (preview != null)
				{
					UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, preview, view.GetDestroyToken()).Forget();
				}

				return;
			}

			if (Table_Item.Get(itemId) != null)
			{
				showConsumableInfoAsync(itemId).Forget();
			}
		}

		// 획득처의 '조합하기'나 사용처 줄로 닫히면 그 제작법으로 넘어간다.
		private async UniTaskVoid showConsumableInfoAsync(int itemId)
		{
			int recipeId = await UIManager.Instance.ShowConsumablePopupAsync(CONSUMABLE_POPUP_ADDRESS, itemId, true, view.GetDestroyToken(), true);
			if (recipeId != 0)
			{
				selectRecipe(recipeId);
			}
		}

		// 티어 올리기 결과는 이전 장비의 등급·강화·품질을 그대로 잇는다. 그 밖에는 정해진 등급(없으면 아이템 기본 등급)에 대표 품질.
		private EquipmentInstance buildPreview(int itemId, ItemGradeType grade)
		{
			if (_stage.isUpgrade == true && _stage.source != null && itemId == _stage.resultItemId)
			{
				EquipmentInstance upgraded = EquipmentFactory.CreateExact(itemId, _stage.source.grade, _stage.source.quality);
				if (upgraded != null)
				{
					upgraded.level = _stage.source.level;
				}

				return upgraded;
			}

			if (grade == ItemGradeType.None)
			{
				Table_Item.Row item = Table_Item.Get(itemId);
				grade = (item != null && item.Grade != ItemGradeType.None) ? item.Grade : ItemGradeType.Normal;
			}

			return EquipmentFactory.CreateExact(itemId, grade, ProjectOne.Reward.RewardPreview.DEFAULT_QUALITY);
		}

		private void showCurrencyTooltip(EDT.Currency currency, ItemCellView cell)
		{
			Table_Currency.Row row = Table_Currency.Get(currency);
			if (row != null)
			{
				UIManager.Instance.ShowTooltipAsync(row.Name, row.Desc, cell.Root, view.GetDestroyToken()).Forget();
			}
		}

		private void onForgeActionClicked()
		{
			if (_pendingGrowth == true || NetworkManager.Instance.IsLoggedIn == false)
			{
				return;
			}

			Table_ForgeRecipe.Row recipe = Table_ForgeRecipe.Get(_selectedRecipeId);
			EquipmentInstance source = pickSource(recipe);
			if (ForgeService.GetBlock(recipe, source, 1) != ForgeBlock.None)
			{
				return;
			}

			ForgeCraftRequest request = new ForgeCraftRequest();
			request.recipeId = recipe.ID;
			request.uid = (source != null) ? source.uid : 0;
			request.count = 1;
			_pendingGrowth = true;
			NetworkManager.Instance.RequestForgeCraft(request, onForgeCraftResponse);
		}

		// 계정 반영은 창이 닫혔어도 한다 — 서버에는 이미 저장됐다.
		private void onForgeCraftResponse(bool success, ForgeCraftResponse data, string error)
		{
			_pendingGrowth = false;
			if (success == false || data == null)
			{
				Debug.LogWarning($"[Craft] 제작 실패: {error}");
				return;
			}

			ForgeService.ApplyCraftResponse(data);
			if (_isDisposed == true)
			{
				return;
			}

			if (data.crafted == false)
			{
				Table_ForgeRecipe.Row recipe = Table_ForgeRecipe.Get(_selectedRecipeId);
				int next = (data.equipment != null) ? ForgeRules.GetSuccessRate(recipe, data.equipment.forgeFails) : 0;
				UIManager.Instance.ShowAlertMessage("제작 실패 — 다음 성공 확률 " + formatRate(next));
			}
			else
			{
				_pendingForgePop = true;
			}

			rebuild();
		}

		// ── 렌더 ──────────────────────────────────────────────────────

		private async UniTask renderForgeAsync(CancellationToken ct)
		{
			buildRecipeRows();
			await view.RenderRecipeListAsync(_recipeRows, _rowCosts, _selectedRecipeId, ct);
			if (ct.IsCancellationRequested == true)
			{
				return;
			}

			Table_ForgeRecipe.Row recipe = Table_ForgeRecipe.Get(_selectedRecipeId);
			if (recipe == null)
			{
				_stage.source = null;
				await view.RenderForgeStageAsync(null, ct);
				return;
			}

			buildStage(recipe);
			await view.RenderForgeStageAsync(_stage, ct);
		}

		// 현재 탭의 조합법을 티어 → ID 순으로 나열한다. 고른 행이 탭에 없으면 첫 행을 고른다.
		private void buildRecipeRows()
		{
			_recipes.Clear();
			Dictionary<int, Table_ForgeRecipe.Row>.Enumerator e = Table_ForgeRecipe.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (matchesRecipeTab(e.Current.Value) == true)
				{
					_recipes.Add(e.Current.Value);
				}
			}

			_recipes.Sort(compareRecipe);

			_recipeRows.Clear();
			_rowCosts.Clear();
			bool selectedFound = false;
			for (int i = 0; i < _recipes.Count; i++)
			{
				Table_ForgeRecipe.Row recipe = _recipes[i];
				Table_Item.Row item = Table_Item.Get(recipe.ResultItemID);
				EquipmentInstance source = pickSource(recipe);

				RecipeRowData row;
				row.recipeId = recipe.ID;
				row.resultItemId = recipe.ResultItemID;
				row.grade = getResultGrade(recipe, source, item);
				row.name = (item != null) ? item.Name : recipe.ResultItemID.ToString();
				row.state = getState(recipe, source);
				row.costStart = _rowCosts.Count;
				addRowCosts(recipe, source);
				row.costCount = _rowCosts.Count - row.costStart;
				_recipeRows.Add(row);

				if (recipe.ID == _selectedRecipeId)
				{
					selectedFound = true;
				}
			}

			if (selectedFound == false)
			{
				_selectedRecipeId = (_recipeRows.Count > 0) ? _recipeRows[0].recipeId : 0;
			}
		}

		private void buildStage(Table_ForgeRecipe.Row recipe)
		{
			ForgeRecipeKind kind = ForgeRules.GetKind(recipe);
			EquipmentInstance source = pickSource(recipe);
			Table_Item.Row item = Table_Item.Get(recipe.ResultItemID);

			_stage.resultItemId = recipe.ResultItemID;
			_stage.resultGrade = getResultGrade(recipe, source, item);
			_stage.resultCount = (kind == ForgeRecipeKind.Material) ? recipe.ResultCount : 1;
			_stage.resultName = (item != null) ? item.Name : recipe.ResultItemID.ToString();

			_stage.isUpgrade = kind == ForgeRecipeKind.Upgrade;
			_stage.source = source;
			_stage.sourceItemId = recipe.SourceItemID;
			Table_Item.Row sourceItem = Table_Item.Get(recipe.SourceItemID);
			_stage.sourceName = (sourceItem != null) ? sourceItem.Name : string.Empty;

			ForgeService.GetItemCosts(recipe, 1, _stage.items);
			ForgeService.GetCurrencyCosts(recipe, 1, _stage.currencies);

			ForgeBlock block = ForgeService.GetBlock(recipe, source, 1);
			_stage.canCraft = block == ForgeBlock.None && _pendingGrowth == false;

			switch (kind)
			{
				case ForgeRecipeKind.Upgrade:
					_stage.rateText = RATE_PREFIX + formatRate(ForgeService.GetSuccessRate(recipe, source));
					_stage.hint = HINT_UPGRADE;
					break;

				case ForgeRecipeKind.Base:
					_stage.rateText = RATE_PREFIX + formatRate(ForgeRules.RateMax);
					_stage.hint = (block == ForgeBlock.InventoryFull) ? "가방이 가득 찼습니다" : HINT_BASE;
					break;

				default:
					_stage.rateText = RATE_PREFIX + formatRate(ForgeRules.RateMax);
					_stage.hint = string.Empty;
					break;
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 행의 재료 그림 — 트리 칸과 같은 순서(이전 장비 · 재료 · 재화)로 _rowCosts 에 붙인다.
		private void addRowCosts(Table_ForgeRecipe.Row recipe, EquipmentInstance source)
		{
			RecipeCostIcon icon;
			if (ForgeRules.GetKind(recipe) == ForgeRecipeKind.Upgrade)
			{
				icon.isCurrency = false;
				icon.itemId = recipe.SourceItemID;
				icon.currency = default;
				icon.ready = source != null;
				_rowCosts.Add(icon);
			}

			ForgeService.GetItemCosts(recipe, 1, _itemCostBuffer);
			for (int i = 0; i < _itemCostBuffer.Count; i++)
			{
				icon.isCurrency = false;
				icon.itemId = _itemCostBuffer[i].itemId;
				icon.currency = default;
				icon.ready = _itemCostBuffer[i].IsEnough;
				_rowCosts.Add(icon);
			}

			ForgeService.GetCurrencyCosts(recipe, 1, _currencyCostBuffer);
			for (int i = 0; i < _currencyCostBuffer.Count; i++)
			{
				icon.isCurrency = true;
				icon.itemId = 0;
				icon.currency = _currencyCostBuffer[i].currency;
				icon.ready = _currencyCostBuffer[i].IsEnough;
				_rowCosts.Add(icon);
			}
		}

		// 지금 분류 탭 · 세부 선택에 들어가는 제작법인가.
		private bool matchesRecipeTab(Table_ForgeRecipe.Row recipe)
		{
			ForgeRecipeKind kind = ForgeRules.GetKind(recipe);
			if (_recipeTab == RECIPE_TAB_MATERIAL)
			{
				return kind == ForgeRecipeKind.Material;
			}

			if (kind == ForgeRecipeKind.Invalid || kind == ForgeRecipeKind.Material || getRecipeTab(recipe) != _recipeTab)
			{
				return false;
			}

			Table_Equipment.Row equipment = Table_Equipment.Get(recipe.ResultItemID);
			switch (_recipeTab)
			{
				case RECIPE_TAB_WEAPON:
					return equipment.WeaponType == SUB_WEAPON_TYPES[_recipeSub];

				case RECIPE_TAB_ARMOR:
					return _recipeSub == 0 || equipment.EquipSlotType == SUB_ARMOR_SLOTS[_recipeSub];

				case RECIPE_TAB_ACCESSORY:
					return _recipeSub == 0 || equipment.EquipSlotType == SUB_ACCESSORY_SLOTS[_recipeSub];
			}

			return true;
		}

		// 티어 오름차순 → ID 오름차순.
		private static int compareRecipe(Table_ForgeRecipe.Row a, Table_ForgeRecipe.Row b)
		{
			int sa = ForgeRules.GetTier(a.ResultItemID);
			int sb = ForgeRules.GetTier(b.ResultItemID);
			if (sa != sb)
			{
				return sa.CompareTo(sb);
			}

			return a.ID.CompareTo(b.ID);
		}

		// 티어 올리기에 쓸 이전 장비 — 장착 중인 것 → 등급 → 강화 → 품질 순으로 가장 좋은 것. 없으면 null.
		private static EquipmentInstance pickSource(Table_ForgeRecipe.Row recipe)
		{
			if (recipe == null || ForgeRules.GetKind(recipe) != ForgeRecipeKind.Upgrade)
			{
				return null;
			}

			IReadOnlyList<EquipmentInstance> all = Account.Instance.Inventory.GetAllEquipments();
			EquipmentInstance best = null;
			for (int i = 0; i < all.Count; i++)
			{
				EquipmentInstance candidate = all[i];
				if (candidate.itemId != recipe.SourceItemID)
				{
					continue;
				}

				if (best == null || isBetterSource(candidate, best) == true)
				{
					best = candidate;
				}
			}

			return best;
		}

		private static bool isBetterSource(EquipmentInstance a, EquipmentInstance b)
		{
			if (a.IsEquipped != b.IsEquipped)
			{
				return a.IsEquipped;
			}

			if (a.grade != b.grade)
			{
				return a.grade > b.grade;
			}

			if (a.level != b.level)
			{
				return a.level > b.level;
			}

			return a.quality > b.quality;
		}

		// 결과 칸 등급 — 티어 올리기는 이전 장비 등급(유지), 기초 제작은 조합법 등급, 재료는 테이블 등급.
		private static ItemGradeType getResultGrade(Table_ForgeRecipe.Row recipe, EquipmentInstance source, Table_Item.Row item)
		{
			ForgeRecipeKind kind = ForgeRules.GetKind(recipe);
			if (kind == ForgeRecipeKind.Upgrade)
			{
				return (source != null) ? source.grade : ItemGradeType.Normal;
			}

			if (kind == ForgeRecipeKind.Base)
			{
				return (recipe.ResultGrade != ItemGradeType.None) ? recipe.ResultGrade : ItemGradeType.Normal;
			}

			return (item != null) ? item.Grade : ItemGradeType.Normal;
		}

		private static RecipeState getState(Table_ForgeRecipe.Row recipe, EquipmentInstance source)
		{
			ForgeBlock block = ForgeService.GetBlock(recipe, source, 1);
			if (block == ForgeBlock.NoSource)
			{
				return RecipeState.Locked;
			}

			return (block == ForgeBlock.None) ? RecipeState.Craftable : RecipeState.Short;
		}

		// ‰ → "60%" (소수 첫째 자리는 0 이 아닐 때만)
		private static string formatRate(int permille)
		{
			if (permille % 10 == 0)
			{
				return (permille / 10).ToString() + "%";
			}

			return (permille / 10).ToString() + "." + (permille % 10).ToString() + "%";
		}
	}
}
