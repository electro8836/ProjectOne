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
	// 제작 모드의 View — 위 조합법 트리(결과 → 재료 줄 → 모자란 하위 재료 한 층 → 제작 버튼)와
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

		// 조합법 행 1개.
		private sealed class RecipeRowView
		{
			public VisualElement root;
			public ItemCellView cell;
			public Label name;
			public Label sub;
			public Label pill;
			public int recipeId;
		}

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnRecipeTabSelected;
		public event Action<int> OnRecipeSelected;
		public event Action OnForgeActionClicked;
		public event Action OnForgeSubClicked;
		public event Action OnForgeSourceClicked;

		private readonly Button[] _recipeTabs = new Button[RECIPE_TAB_COUNT];
		private readonly List<ForgeMatView> _matViews = new List<ForgeMatView>();
		private readonly List<ForgeMatView> _subViews = new List<ForgeMatView>();
		private readonly List<RecipeRowView> _rows = new List<RecipeRowView>();
		private readonly List<UniTask> _forgeTasks = new List<UniTask>();

		private VisualElement _forgeStage;
		private ForgeMatView _resultView;
		private Label _resultName;
		private VisualElement _forgeMats;
		private VisualElement _forgeBranch;
		private VisualElement _forgeSub;
		private VisualElement _forgeSubCells;
		private Button _forgeAction;
		private Label _forgeActionTitle;
		private Label _forgeActionRate;
		private Label _forgeHint;
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
			_forgeSub = root.Q("forge-sub");
			_forgeSubCells = root.Q("forge-sub-cells");
			root.Q<Button>("forge-sub-button").clicked += onForgeSubClicked;

			_forgeAction = root.Q<Button>("forge-action");
			_forgeAction.clicked += onForgeActionClicked;
			_forgeActionTitle = root.Q<Label>("forge-action-title");
			_forgeActionRate = root.Q<Label>("forge-action-rate");
			_forgeHint = root.Q<Label>("forge-hint");

			_recipeList = root.Q<ScrollView>("recipe-list");
			_recipeList.AddManipulator(new ScrollDragManipulator(_recipeList));

			_reforgeInfo = root.Q<Label>("reforge-info");
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		public void SelectRecipeTab(int index)
		{
			selectTabs(_recipeTabs, index, RECIPE_TAB_ON);
		}

		public void SetReforgeInfo(string text)
		{
			_reforgeInfo.text = text;
		}

		public async UniTask RenderRecipeListAsync(IReadOnlyList<RecipeRowData> data, int selectedId, CancellationToken ct)
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
				row.sub.text = d.sub;
				applyPill(row.pill, d.state);
				_forgeTasks.Add(bindItemCell(row.cell, d.resultItemId, d.grade, ct));
			}

			for (int i = data.Count; i < _rows.Count; i++)
			{
				setShown(_rows[i].root, false);
			}

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
				view.cell.Tag = "source";
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

			// 하위 재료 한 층
			_forgeSub.style.visibility = (data.subItems.Count > 0) ? Visibility.Visible : Visibility.Hidden;
			for (int i = 0; i < data.subItems.Count; i++)
			{
				ForgeMatView view = getOrCreateMat(_subViews, _forgeSubCells, i);
				bindMaterial(view, data.subItems[i].itemId, data.subItems[i].owned, data.subItems[i].count, ct);
				view.name.text = view.name.text + " x" + data.subItems[i].count;
			}

			hideFrom(_subViews, data.subItems.Count);

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
			view.root.RemoveFromClassList("forge-mat--owned");
			view.cell.Tag = itemId;
			_forgeTasks.Add(bindItemCell(view.cell, itemId, ItemGradeType.None, ct));
		}

		private void bindCurrency(ForgeMatView view, EDT.Currency currency, int owned, int need, CancellationToken ct)
		{
			Table_Currency.Row row = Table_Currency.Get(currency);
			view.name.text = (row != null) ? row.Name : currency.ToString();
			view.count.text = CurrencyFormat.Short(owned) + "/" + CurrencyFormat.Short(need);
			view.root.EnableInClassList("forge-mat--short", owned < need);
			view.root.RemoveFromClassList("forge-mat--owned");
			view.cell.Tag = null;
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
				if (parent == _forgeSubCells)
				{
					box.AddToClassList("forge-cell--sub");
					stub.style.display = DisplayStyle.None;
				}

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
			row.sub = new Label();
			row.sub.AddToClassList("recipe-row__sub");
			texts.Add(row.sub);

			row.pill = new Label();
			row.pill.AddToClassList("recipe-pill");
			row.pill.pickingMode = PickingMode.Ignore;
			row.root.Add(row.pill);

			_rows.Add(row);
			return row;
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onRecipeTabClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			SelectRecipeTab(index);
			_recipeList.scrollOffset = Vector2.zero;
			if (OnRecipeTabSelected != null) { OnRecipeTabSelected.Invoke(index); }
		}

		private void onRecipeRowClicked(ClickEvent evt)
		{
			RecipeRowView row = (RecipeRowView)((VisualElement)evt.currentTarget).userData;
			if (OnRecipeSelected != null) { OnRecipeSelected.Invoke(row.recipeId); }
		}

		// 트리의 칸 — 이전 장비 칸만 반응한다(장비 정보).
		private void onForgeCellClicked(ItemCellView sender, long uid, int itemId)
		{
			if (uid == 0 || OnForgeSourceClicked == null)
			{
				return;
			}

			OnForgeSourceClicked.Invoke();
		}

		private void onForgeActionClicked()
		{
			if (OnForgeActionClicked != null) { OnForgeActionClicked.Invoke(); }
		}

		private void onForgeSubClicked()
		{
			if (OnForgeSubClicked != null) { OnForgeSubClicked.Invoke(); }
		}
	}
}
