using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 제작 모드의 View — 위 조합법 트리(결과 → 재료 줄 → 제작 버튼)와
	// 아래 조합법 목록. 표시와 입력 전달만 하고, 무엇을 그릴지는 CraftPresenter(Forge) 가 정한다.
	public partial class CraftUI
	{
		private const int RECIPE_TAB_COUNT = 5;
		private const string RECIPE_TAB_ON = "tab--selected";

		// 재료 칸 1개 — 칸(ItemCellView) 위에 보유량/필요량·체크·부족 테두리를 얹고 아래에 이름을 단다.
		private sealed class ForgeMatView
		{
			public VisualElement root;
			public ItemCellView cell;
			public Label count;
			public Label name;
		}

		private const string RECIPE_SUB_ON = "recipe-sub--on";
		private const string RECIPE_SUB_NOSEP = "recipe-sub--nosep";

		// 조합법 행의 재료 그림 1칸 — 갖춘 칸은 밝게 + 초록 체크, 모자란 칸은 흐리게 + 빨간 테두리.
		private sealed class RecipeCostView
		{
			public VisualElement root;
			public VisualElement icon;
		}

		// 조합법 행 1개.
		private sealed class RecipeRowView
		{
			public VisualElement root;
			public ItemCellView cell;
			public Label name;
			public VisualElement costs;
			public readonly List<RecipeCostView> costViews = new List<RecipeCostView>(6);
			public Label pill;
			public int recipeId;
		}

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnRecipeTabSelected;
		public event Action<int> OnRecipeSubSelected;
		public event Action<int> OnRecipeSelected;
		public event Action OnForgeActionClicked;
		public event Action OnMaterialListClicked;
		public event Action<ItemCellView> OnForgeCellClicked;	// 트리의 칸 — 무엇인지는 칸의 Tag(FORGE_TAG_* · 재료 itemId · 재화)로 가린다

		// 트리 칸 Tag — 재료 칸은 itemId(int), 재화 칸은 EDT.Currency 를 싣는다
		public const string FORGE_TAG_RESULT = "result";
		public const string FORGE_TAG_SOURCE = "source";

		private readonly Button[] _recipeTabs = new Button[RECIPE_TAB_COUNT];
		private readonly List<ForgeMatView> _matViews = new List<ForgeMatView>();
		private readonly List<RecipeRowView> _rows = new List<RecipeRowView>();
		private readonly List<Button> _subButtons = new List<Button>(5);
		private readonly List<UniTask> _forgeTasks = new List<UniTask>();

		private VisualElement _forgeStage;
		private ForgeMatView _resultView;
		private Label _resultName;
		private VisualElement _forgeMats;
		private VisualElement _forgeBranch;
		private Button _forgeAction;
		private Label _forgeActionTitle;
		private Label _forgeActionRate;
		private Label _forgeHint;
		private VisualElement _recipeSubs;
		private Label _recipeEmpty;
		private ScrollView _recipeList;
		private Label _reforgeInfo;

		private void bindForge(VisualElement root)
		{
			for (int i = 0; i < RECIPE_TAB_COUNT; i++)
			{
				Button tab = root.Q<Button>("recipe-tab-" + i);
				tab.userData = i;
				tab.RegisterCallback<ClickEvent>(onRecipeTabClicked);
				_recipeTabs[i] = tab;
			}

			_forgeStage = root.Q("forge-stage");
			_resultView = createMatView(root.Q("forge-result"), false);
			_resultName = root.Q<Label>("forge-result-name");
			_forgeMats = root.Q("forge-mats");
			_forgeBranch = root.Q("forge-branch");
			root.Q<Button>("forge-material-button").clicked += onMaterialListClicked;

			_forgeAction = root.Q<Button>("forge-action");
			_forgeAction.clicked += onForgeActionClicked;
			_forgeActionTitle = root.Q<Label>("forge-action-title");
			_forgeActionRate = root.Q<Label>("forge-action-rate");
			_forgeHint = root.Q<Label>("forge-hint");

			_recipeSubs = root.Q("recipe-subs");
			_recipeEmpty = root.Q<Label>("recipe-empty");
			_recipeList = root.Q<ScrollView>("recipe-list");
			_recipeList.AddManipulator(new ScrollDragManipulator(_recipeList));

			_reforgeInfo = root.Q<Label>("reforge-info");
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		public void SelectRecipeTab(int index)
		{
			selectTabs(_recipeTabs, index, RECIPE_TAB_ON);
		}

		// 분류 탭 아래 세부 선택(분할 막대). labels 가 비면 막대를 감춘다.
		public void SetRecipeSubs(IReadOnlyList<string> labels, int selected)
		{
			setShown(_recipeSubs, labels.Count > 0);
			for (int i = 0; i < labels.Count; i++)
			{
				Button button = getOrCreateSub(i);
				button.text = labels[i];
				setShown(button, true);
			}

			for (int i = labels.Count; i < _subButtons.Count; i++)
			{
				setShown(_subButtons[i], false);
			}

			selectSub(selected);
		}

		public void SetReforgeInfo(string text)
		{
			_reforgeInfo.text = text;
		}

		public async UniTask RenderRecipeListAsync(IReadOnlyList<RecipeRowData> data, IReadOnlyList<RecipeCostIcon> costs, int selectedId, CancellationToken ct)
		{
			_forgeTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				RecipeRowView row = getOrCreateRow(i);
				RecipeRowData d = data[i];
				row.recipeId = d.recipeId;
				setShown(row.root, true);
				row.root.EnableInClassList("recipe-row--selected", d.recipeId == selectedId);
				row.name.text = d.name;
				applyPill(row.pill, d.state);
				applyCosts(row, costs, d.costStart, d.costCount, ct);
				_forgeTasks.Add(bindItemCell(row.cell, d.resultItemId, d.grade, ct));
			}

			for (int i = data.Count; i < _rows.Count; i++)
			{
				setShown(_rows[i].root, false);
			}

			setShown(_recipeEmpty, data.Count == 0);

			await UniTask.WhenAll(_forgeTasks).SuppressCancellationThrow();
		}

		// 트리 — data 가 null 이면 안내 글자만 보인다.
		public async UniTask RenderForgeStageAsync(ForgeStageData data, CancellationToken ct)
		{
			_forgeStage.EnableInClassList("forge-stage--empty", data == null);
			if (data == null)
			{
				return;
			}

			_forgeTasks.Clear();

			// 결과
			_resultName.text = data.resultName;
			_resultView.count.text = (data.resultCount > 1) ? "x" + data.resultCount : string.Empty;
			_resultView.cell.Tag = FORGE_TAG_RESULT;
			_forgeTasks.Add(bindItemCell(_resultView.cell, data.resultItemId, data.resultGrade, ct));

			// 재료 줄 — 이전 장비 · 재료 · 재화 순
			int column = 0;
			if (data.isUpgrade == true)
			{
				ForgeMatView view = getOrCreateMat(_matViews, _forgeMats, column);
				column++;
				view.root.EnableInClassList("forge-mat--owned", data.source != null);
				view.root.EnableInClassList("forge-mat--short", data.source == null);
				view.name.text = data.sourceName;
				view.count.text = string.Empty;
				view.cell.Tag = FORGE_TAG_SOURCE;
				if (data.source != null)
				{
					_forgeTasks.Add(view.cell.BindEquipmentAsync(data.source, false, ct));
					view.cell.HideLevel();	// 칸이 작아 티어 배지와 겹친다 — 강화 레벨은 장비 정보에서 본다
				}
				else
				{
					_forgeTasks.Add(bindItemCell(view.cell, data.sourceItemId, ItemGradeType.Normal, ct));
				}
			}

			for (int i = 0; i < data.items.Count; i++)
			{
				ForgeMatView view = getOrCreateMat(_matViews, _forgeMats, column);
				column++;
				bindMaterial(view, data.items[i].itemId, data.items[i].owned, data.items[i].count, ct);
			}

			for (int i = 0; i < data.currencies.Count; i++)
			{
				ForgeMatView view = getOrCreateMat(_matViews, _forgeMats, column);
				column++;
				bindCurrency(view, data.currencies[i].currency, data.currencies[i].owned, data.currencies[i].amount, ct);
			}

			hideFrom(_matViews, column);
			setShown(_forgeBranch, column > 1);

			// 버튼
			_forgeAction.SetEnabled(data.canCraft);
			_forgeActionRate.text = data.rateText;
			setShown(_forgeActionRate, string.IsNullOrEmpty(data.rateText) == false);
			_forgeHint.text = data.hint;

			await UniTask.WhenAll(_forgeTasks).SuppressCancellationThrow();
		}

		// 제작 성공 강조 — 결과 칸을 키웠다가 제자리로.
		public void PlayForgePop()
		{
			VisualElement box = _resultView.root;
			box.AddToClassList("craft-slot--pop");
			box.schedule.Execute(clearForgePop);
		}

		// ── 내부: 표시 ────────────────────────────────────────────────────

		private void bindMaterial(ForgeMatView view, int itemId, int owned, int need, CancellationToken ct)
		{
			Table_Item.Row row = Table_Item.Get(itemId);
			view.name.text = (row != null) ? row.Name : itemId.ToString();
			view.count.text = owned + "/" + need;
			view.root.EnableInClassList("forge-mat--short", owned < need);
			view.root.EnableInClassList("forge-mat--owned", owned >= need);	// 다 갖추면 초록 체크(목록의 재료 그림과 같게)
			view.cell.Tag = itemId;
			_forgeTasks.Add(bindItemCell(view.cell, itemId, ItemGradeType.None, ct));
		}

		private void bindCurrency(ForgeMatView view, EDT.Currency currency, int owned, int need, CancellationToken ct)
		{
			Table_Currency.Row row = Table_Currency.Get(currency);
			view.name.text = (row != null) ? row.Name : currency.ToString();
			view.count.text = CurrencyFormat.Short(owned) + "/" + CurrencyFormat.Short(need);
			view.root.EnableInClassList("forge-mat--short", owned < need);
			view.root.EnableInClassList("forge-mat--owned", owned >= need);
			view.cell.Tag = currency;
			_forgeTasks.Add(bindCurrencyCell(view.cell, currency, ct));
		}

		// 테이블 행으로 칸을 그린다 — grade 가 None 이 아니면 등급 틀을 그 등급으로 칠한다. 개수 글자는 쓰지 않는다.
		private UniTask bindItemCell(ItemCellView cell, int itemId, ItemGradeType grade, CancellationToken ct)
		{
			UniTask task = cell.BindItemAsync(Table_Item.Get(itemId), 1, ct);
			if (grade != ItemGradeType.None)
			{
				cell.SetGrade(grade);
			}

			return task;
		}

		private UniTask bindCurrencyCell(ItemCellView cell, EDT.Currency currency, CancellationToken ct)
		{
			UniTask task = cell.BindCurrencyAsync(currency, 0, ct);
			cell.SetCountText(string.Empty);
			return task;
		}

		// 행의 재료 그림 — 칸 수만큼 쓰고 남는 칸은 감춘다.
		private void applyCosts(RecipeRowView row, IReadOnlyList<RecipeCostIcon> costs, int start, int count, CancellationToken ct)
		{
			for (int i = 0; i < count; i++)
			{
				RecipeCostView view = getOrCreateCost(row, i);
				RecipeCostIcon cost = costs[start + i];
				setShown(view.root, true);
				view.root.EnableInClassList("recipe-cost--ready", cost.ready);
				view.root.EnableInClassList("recipe-cost--short", cost.ready == false);
				_forgeTasks.Add(_icons.SetAsync(view.icon, getCostIconAddress(cost), ct));
			}

			for (int i = count; i < row.costViews.Count; i++)
			{
				setShown(row.costViews[i].root, false);
			}
		}

		private static string getCostIconAddress(RecipeCostIcon cost)
		{
			if (cost.isCurrency == true)
			{
				Table_Currency.Row currency = Table_Currency.Get(cost.currency);
				return (currency != null) ? currency.Icon : string.Empty;
			}

			Table_Item.Row item = Table_Item.Get(cost.itemId);
			return (item != null) ? item.Icon : string.Empty;
		}

		private void selectSub(int selected)
		{
			for (int i = 0; i < _subButtons.Count; i++)
			{
				Button button = _subButtons[i];
				button.EnableInClassList(RECIPE_SUB_ON, i == selected);
				// 구분선은 고른 칸 양옆과 첫 칸에서 뺀다
				button.EnableInClassList(RECIPE_SUB_NOSEP, i == 0 || i == selected || i == selected + 1);
			}
		}

		private static void applyPill(Label pill, RecipeState state)
		{
			pill.EnableInClassList("recipe-pill--ok", state == RecipeState.Craftable);
			pill.EnableInClassList("recipe-pill--short", state == RecipeState.Short);
			switch (state)
			{
				case RecipeState.Craftable:
					pill.text = "제작 가능";
					break;

				case RecipeState.Short:
					pill.text = "재료 부족";
					break;

				default:
					pill.text = "잠김";
					break;
			}
		}

		private static void hideFrom(List<ForgeMatView> views, int start)
		{
			for (int i = start; i < views.Count; i++)
			{
				setShown(views[i].root, false);
			}
		}

		private void clearForgePop()
		{
			_resultView.root.RemoveFromClassList("craft-slot--pop");
		}

		// ── 내부: 풀 ──────────────────────────────────────────────────────

		private ForgeMatView getOrCreateMat(List<ForgeMatView> pool, VisualElement parent, int index)
		{
			ForgeMatView view;
			if (index < pool.Count)
			{
				view = pool[index];
			}
			else
			{
				VisualElement column = new VisualElement();
				column.AddToClassList("forge-mat");
				parent.Add(column);

				VisualElement stub = new VisualElement();
				stub.AddToClassList("forge-mat__stub");
				stub.pickingMode = PickingMode.Ignore;
				column.Add(stub);

				VisualElement box = new VisualElement();
				box.AddToClassList("forge-cell");
				column.Add(box);
				view = createMatView(box, true);
				view.root = column;
				view.name = new Label();
				view.name.AddToClassList("forge-name");
				view.name.pickingMode = PickingMode.Ignore;
				column.Add(view.name);
				pool.Add(view);
			}

			setShown(view.root, true);
			return view;
		}

		// 칸 상자 안에 ItemCellView + 얹는 표시들을 만든다. root 는 상자 자신(결과 칸) — 재료 칸은 호출자가 열로 바꾼다.
		private ForgeMatView createMatView(VisualElement box, bool withShort)
		{
			ForgeMatView view = new ForgeMatView();
			view.root = box;
			view.cell = new ItemCellView(box, _gradeColors, _icons);
			view.cell.OnClicked += onForgeCellClicked;

			if (withShort == true)
			{
				VisualElement shortFrame = new VisualElement();
				shortFrame.AddToClassList("forge-cell__short");
				shortFrame.pickingMode = PickingMode.Ignore;
				box.Add(shortFrame);

				Label check = new Label("✓");
				check.AddToClassList("forge-check");
				check.pickingMode = PickingMode.Ignore;
				box.Add(check);
			}

			view.count = new Label();
			view.count.AddToClassList("forge-count");
			view.count.pickingMode = PickingMode.Ignore;
			box.Add(view.count);
			return view;
		}

		private RecipeRowView getOrCreateRow(int index)
		{
			if (index < _rows.Count)
			{
				return _rows[index];
			}

			RecipeRowView row = new RecipeRowView();
			row.root = new VisualElement();
			row.root.AddToClassList("recipe-row");
			row.root.AddToClassList("sfx-click");
			row.root.userData = row;
			row.root.RegisterCallback<ClickEvent>(onRecipeRowClicked);
			_recipeList.contentContainer.Add(row.root);

			VisualElement box = new VisualElement();
			box.AddToClassList("forge-cell");
			box.pickingMode = PickingMode.Ignore;
			row.root.Add(box);
			row.cell = new ItemCellView(box, _gradeColors, _icons);
			row.cell.Root.pickingMode = PickingMode.Ignore;

			VisualElement texts = new VisualElement();
			texts.AddToClassList("recipe-row__texts");
			texts.pickingMode = PickingMode.Ignore;
			row.root.Add(texts);
			row.name = new Label();
			row.name.AddToClassList("recipe-row__name");
			texts.Add(row.name);
			row.costs = new VisualElement();
			row.costs.AddToClassList("recipe-costs");
			row.costs.pickingMode = PickingMode.Ignore;
			texts.Add(row.costs);

			row.pill = new Label();
			row.pill.AddToClassList("recipe-pill");
			row.pill.pickingMode = PickingMode.Ignore;
			row.root.Add(row.pill);

			_rows.Add(row);
			return row;
		}

		private static RecipeCostView getOrCreateCost(RecipeRowView row, int index)
		{
			if (index < row.costViews.Count)
			{
				return row.costViews[index];
			}

			RecipeCostView view = new RecipeCostView();
			view.root = new VisualElement();
			view.root.AddToClassList("recipe-cost");
			view.root.pickingMode = PickingMode.Ignore;
			row.costs.Add(view.root);

			view.icon = new VisualElement();
			view.icon.AddToClassList("recipe-cost__icon");
			view.icon.pickingMode = PickingMode.Ignore;
			view.root.Add(view.icon);

			Label check = new Label("✓");
			check.AddToClassList("recipe-cost__check");
			check.pickingMode = PickingMode.Ignore;
			view.root.Add(check);

			row.costViews.Add(view);
			return view;
		}

		private Button getOrCreateSub(int index)
		{
			if (index < _subButtons.Count)
			{
				return _subButtons[index];
			}

			Button button = new Button();
			button.AddToClassList("recipe-sub");
			button.AddToClassList("sfx-click");
			button.userData = index;
			button.RegisterCallback<ClickEvent>(onRecipeSubClicked);
			_recipeSubs.Add(button);
			_subButtons.Add(button);
			return button;
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onRecipeTabClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			SelectRecipeTab(index);
			_recipeList.scrollOffset = Vector2.zero;
			if (OnRecipeTabSelected != null) { OnRecipeTabSelected.Invoke(index); }
		}

		private void onRecipeSubClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			selectSub(index);
			_recipeList.scrollOffset = Vector2.zero;
			if (OnRecipeSubSelected != null) { OnRecipeSubSelected.Invoke(index); }
		}

		private void onRecipeRowClicked(ClickEvent evt)
		{
			RecipeRowView row = (RecipeRowView)((VisualElement)evt.currentTarget).userData;
			if (OnRecipeSelected != null) { OnRecipeSelected.Invoke(row.recipeId); }
		}

		// 트리의 칸 — 이전 장비 칸만 반응한다(장비 정보).
		private void onForgeCellClicked(ItemCellView sender, long uid, int itemId)
		{
			if (OnForgeCellClicked != null)
			{
				OnForgeCellClicked.Invoke(sender);
			}
		}

		private void onForgeActionClicked()
		{
			if (OnForgeActionClicked != null) { OnForgeActionClicked.Invoke(); }
		}

		private void onMaterialListClicked()
		{
			if (OnMaterialListClicked != null) { OnMaterialListClicked.Invoke(); }
		}
	}
}
