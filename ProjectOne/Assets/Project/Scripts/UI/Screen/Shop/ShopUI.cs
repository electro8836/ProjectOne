using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Shop;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 상점 화면의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/Screens/Shop/ShopScreen.uxml, 상품 칸은 ShopProductCell.
	// 네비게이션 바의 상점 탭이 UIPrefab_Shop 을 창 층에 연다.
	//
	// 탭은 ShopCategory 테이블이 정한다. 목록은 그룹 제목 아래에 그 그룹의 상품을 쌓는 구조를 반복한다 —
	// 낱개 상품 그룹은 3열 그리드에, 나머지는 한 건이 화면 폭을 통째로 쓴다.
	public class ShopUI : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action<int> OnTabChanged;
		public event Action<int> OnBuyClicked;
		public event Action OnHomeClicked;

		private readonly ShopPresenter _presenter = new ShopPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		// 종류별 칸 풀 — 탭을 바꿀 때 다시 만들지 않고 재사용한다.
		private sealed class CellPool
		{
			public readonly List<ShopProductCell> cells = new List<ShopProductCell>();
			public int cursor;
		}

		private readonly Dictionary<ShopCellKind, CellPool> _pools = new Dictionary<ShopCellKind, CellPool>();
		private readonly List<Label> _titles = new List<Label>();
		private readonly List<VisualElement> _grids = new List<VisualElement>();
		private int _titleCursor;
		private int _gridCursor;

		private VisualElement _tabBar;
		private readonly List<Button> _tabs = new List<Button>();
		private ScrollView _list;

		protected override void OnBind(VisualElement root)
		{
			_tabBar = root.Q("tabs");
			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));
			root.Q<Button>("home-button").clicked += onHomeClicked;

			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_presenter.Dispose();
			}

			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		public override UniTask OnCloseAsync()
		{
			return _presenter.OnCloseAsync();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		// 카테고리 수만큼 탭을 만든다.
		public void SetCategories(IReadOnlyList<Table_ShopCategory.Row> categories)
		{
			for (int i = 0; i < categories.Count; i++)
			{
				Button tab = getTab(i);
				tab.text = categories[i].Name;
				tab.style.display = DisplayStyle.Flex;
			}

			for (int i = categories.Count; i < _tabs.Count; i++)
			{
				_tabs[i].style.display = DisplayStyle.None;
			}
		}

		// 탭만 선택(OnTabChanged 를 발행하지 않음).
		public void SelectTab(int index)
		{
			for (int i = 0; i < _tabs.Count; i++)
			{
				_tabs[i].EnableInClassList("tab--selected", i == index);
			}
		}

		public async UniTask RenderCategoryAsync(ShopCategory category, CancellationToken ct)
		{
			_bindTasks.Clear();
			resetCursors();

			VisualElement content = _list.contentContainer;
			int order = 0;

			IReadOnlyList<Table_ShopGoodsGroup.Row> groups = ShopCatalog.GetGroups(category);
			for (int g = 0; g < groups.Count; g++)
			{
				IReadOnlyList<Table_ShopGoods.Row> goods = ShopCatalog.GetGoods(groups[g].ID);
				if (goods.Count == 0)
				{
					continue;
				}

				Label title = acquireTitle();
				title.text = groups[g].Name;
				place(title, content, order);
				order++;

				// 한 그룹의 상품은 표시 형태가 같다 — 첫 상품의 종류로 그룹 전체의 배치를 정한다.
				bool useGrid = toKind(goods[0].GoodsType) == ShopCellKind.Single;
				VisualElement grid = null;
				if (useGrid == true)
				{
					grid = acquireGrid();
					place(grid, content, order);
					order++;
				}

				int gridOrder = 0;
				for (int i = 0; i < goods.Count; i++)
				{
					ShopProductCell cell = acquireCell(toKind(goods[i].GoodsType));
					if (useGrid == true)
					{
						place(cell.Root, grid, gridOrder);
						gridOrder++;
					}
					else
					{
						place(cell.Root, content, order);
						order++;
					}

					_bindTasks.Add(cell.BindAsync(goods[i], ct));
				}
			}

			hideUnused();
			_list.scrollOffset = Vector2.zero;

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 구매 직후 그 상품의 남은 횟수 표시만 갱신한다.
		public void RefreshGoods(int goodsId)
		{
			Dictionary<ShopCellKind, CellPool>.Enumerator e = _pools.GetEnumerator();
			while (e.MoveNext() == true)
			{
				List<ShopProductCell> cells = e.Current.Value.cells;
				for (int i = 0; i < cells.Count; i++)
				{
					if (cells[i].GoodsId == goodsId && cells[i].Root.resolvedStyle.display == DisplayStyle.Flex)
					{
						cells[i].RefreshPurchaseLimit();
					}
				}
			}
		}

		// ── 내부: 풀 ──────────────────────────────────────────────────────

		private static ShopCellKind toKind(GoodsType type)
		{
			switch (type)
			{
				case GoodsType.Box:
					return ShopCellKind.Box;

				case GoodsType.Package:
					return ShopCellKind.Package;

				case GoodsType.HeroPass:
					return ShopCellKind.Pass;

				case GoodsType.NoAd:
					return ShopCellKind.NoAd;
			}

			return ShopCellKind.Single;
		}

		private Button getTab(int index)
		{
			if (index < _tabs.Count)
			{
				return _tabs[index];
			}

			Button tab = new Button();
			tab.AddToClassList("tab");
			tab.userData = index;
			tab.RegisterCallback<ClickEvent>(onTabClicked);
			_tabBar.Add(tab);
			_tabs.Add(tab);
			return tab;
		}

		private void resetCursors()
		{
			_titleCursor = 0;
			_gridCursor = 0;

			Dictionary<ShopCellKind, CellPool>.Enumerator e = _pools.GetEnumerator();
			while (e.MoveNext() == true)
			{
				e.Current.Value.cursor = 0;
			}
		}

		private Label acquireTitle()
		{
			if (_titleCursor < _titles.Count)
			{
				return _titles[_titleCursor++];
			}

			Label title = new Label();
			title.AddToClassList("shop-group-title");
			_titles.Add(title);
			_titleCursor++;
			return title;
		}

		private VisualElement acquireGrid()
		{
			if (_gridCursor < _grids.Count)
			{
				return _grids[_gridCursor++];
			}

			VisualElement grid = new VisualElement();
			grid.AddToClassList("shop-grid");
			_grids.Add(grid);
			_gridCursor++;
			return grid;
		}

		private ShopProductCell acquireCell(ShopCellKind kind)
		{
			CellPool pool;
			if (_pools.TryGetValue(kind, out pool) == false)
			{
				pool = new CellPool();
				_pools.Add(kind, pool);
			}

			if (pool.cursor < pool.cells.Count)
			{
				return pool.cells[pool.cursor++];
			}

			ShopProductCell cell = new ShopProductCell(kind, _gradeColors, _icons, GetDestroyToken());
			cell.OnBuyClicked += onCellBuyClicked;
			pool.cells.Add(cell);
			pool.cursor++;
			return cell;
		}

		// 세로 목록의 순서는 자식 순서가 정한다 — 재사용한 요소는 부모와 순서를 매번 다시 잡는다.
		private static void place(VisualElement element, VisualElement parent, int index)
		{
			if (element.parent != parent)
			{
				element.RemoveFromHierarchy();
			}

			if (index < parent.childCount)
			{
				parent.Insert(index, element);
			}
			else
			{
				parent.Add(element);
			}

			element.style.display = DisplayStyle.Flex;
		}

		private void hideUnused()
		{
			for (int i = _titleCursor; i < _titles.Count; i++)
			{
				_titles[i].style.display = DisplayStyle.None;
			}

			for (int i = _gridCursor; i < _grids.Count; i++)
			{
				_grids[i].style.display = DisplayStyle.None;
			}

			Dictionary<ShopCellKind, CellPool>.Enumerator e = _pools.GetEnumerator();
			while (e.MoveNext() == true)
			{
				CellPool pool = e.Current.Value;
				for (int i = pool.cursor; i < pool.cells.Count; i++)
				{
					pool.cells[i].SetShown(false);
				}
			}
		}

		// ── 내부: 입력 ────────────────────────────────────────────────────

		private void onTabClicked(ClickEvent evt)
		{
			Button tab = evt.currentTarget as Button;
			int index = (int)tab.userData;
			SelectTab(index);

			if (OnTabChanged != null)
			{
				OnTabChanged.Invoke(index);
			}
		}

		private void onCellBuyClicked(int goodsId)
		{
			if (OnBuyClicked != null)
			{
				OnBuyClicked.Invoke(goodsId);
			}
		}

		private void onHomeClicked()
		{
			if (OnHomeClicked != null)
			{
				OnHomeClicked.Invoke();
			}
		}
	}
}
