using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Shop;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 상점 화면의 View(MVP). 네비게이션 바의 상점 탭이 UIPrefab_Shop 을 창 캔버스에 연다.
	//
	// 탭은 ShopCategory 테이블이 정한다 — 이름을 바꿔 달고, 테이블에 없거나 꺼진 카테고리의 버튼은 숨긴다.
	// Content 는 그룹 제목(ShopTitleSlot) 아래에 그 그룹의 상품을 쌓는 구조를 반복한다.
	// 어떤 프리펩으로 그릴지는 GoodsType 이 결정한다.
	public class ShopUI : UIScreen, IView
	{
		[Header("탭")]
		[SerializeField] private TabGroup _tabGroup;			// Top/Frame/Tab/TabButtons
		[SerializeField] private UITabButton[] _tabButtons;		// TabButton_* (ShopCategory.Order 순서로 배열)

		[Header("목록")]
		[SerializeField] private ScrollRect _scrollRect;		// Bottom_ScrollRect
		[SerializeField] private RectTransform _content;		// Bottom_ScrollRect/Viewport/Content
		[SerializeField] private ShopTitleSlot _titlePrefab;		// UIPrefab_ShopProductTitleSlot
		[SerializeField] private RectTransform _gridPrefab;		// UIPrefab_ShopProductGridSlot

		[Header("상품 슬롯")]
		[SerializeField] private ShopSingleSlot _singleSlotPrefab;	// UIPrefab_ShopProductSingleSlot
		[SerializeField] private ShopBoxSlot _boxSlotPrefab;		// UIPrefab_ShopProductBoxSlot
		[SerializeField] private ShopPackageSlot _packageSlotPrefab;	// UIPrefab_ShopProductPackageSlot
		[SerializeField] private ShopPassSlot _passSlotPrefab;		// UIPrefab_ShopProductPassSlot
		[SerializeField] private ShopNoAdSlot _noAdSlotPrefab;		// UIPrefab_ShopProductNoAdSlot

		[Header("닫기")]
		[SerializeField] private UIButton _homeButton;			// Top/HomeButton

		public event Action<int> OnTabChanged;
		public event Action<int> OnBuyClicked;
		public event Action OnHomeClicked;

		// GoodsType 하나에 대응하는 슬롯 프리펩과 그 재사용 캐시.
		private sealed class SlotCache
		{
			public ShopProductSlotBase prefab;
			public readonly List<ShopProductSlotBase> slots = new List<ShopProductSlotBase>();
			public int cursor;
		}

		private readonly Dictionary<GoodsType, SlotCache> _slotCaches = new Dictionary<GoodsType, SlotCache>();
		private readonly List<ShopTitleSlot> _titleSlots = new List<ShopTitleSlot>();
		private readonly List<RectTransform> _grids = new List<RectTransform>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		private int _titleCursor;
		private int _gridCursor;

		private readonly ShopPresenter _presenter = new ShopPresenter();

		private void Awake()
		{
			registerSlotPrefab(GoodsType.Currency, _singleSlotPrefab);
			registerSlotPrefab(GoodsType.Item, _singleSlotPrefab);
			registerSlotPrefab(GoodsType.Box, _boxSlotPrefab);
			registerSlotPrefab(GoodsType.Package, _packageSlotPrefab);
			registerSlotPrefab(GoodsType.HeroPass, _passSlotPrefab);
			registerSlotPrefab(GoodsType.NoAd, _noAdSlotPrefab);

			if (_tabGroup != null)
			{
				_tabGroup.OnTabChanged += onTabChanged;
			}

			if (_homeButton != null)
			{
				_homeButton.OnClickEvent += onHomeClicked;
			}

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			if (_tabGroup != null)
			{
				_tabGroup.OnTabChanged -= onTabChanged;
			}

			if (_homeButton != null)
			{
				_homeButton.OnClickEvent -= onHomeClicked;
			}

			Dictionary<GoodsType, SlotCache>.Enumerator e = _slotCaches.GetEnumerator();
			while (e.MoveNext() == true)
			{
				List<ShopProductSlotBase> slots = e.Current.Value.slots;
				for (int i = 0; i < slots.Count; i++)
				{
					slots[i].OnBuyClicked -= onSlotBuyClicked;
				}
			}

			_presenter.Dispose();
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

		// 탭 버튼에 카테고리 이름을 달고, 카테고리 수를 넘는 버튼은 숨긴다.
		// 버튼이 카테고리보다 적으면 뒤쪽 카테고리는 표시되지 않는다(프리펩에 버튼을 늘려야 한다).
		public void SetCategories(IReadOnlyList<Table_ShopCategory.Row> categories)
		{
			if (_tabButtons == null)
			{
				return;
			}

			for (int i = 0; i < _tabButtons.Length; i++)
			{
				UITabButton button = _tabButtons[i];
				if (button == null)
				{
					continue;
				}

				bool used = i < categories.Count;
				button.gameObject.SetActive(used);

				if (used == true)
				{
					button.SetLabel(categories[i].Name);
				}
			}
		}

		public void SelectTab(int index)
		{
			if (_tabGroup != null)
			{
				_tabGroup.Select(index);
			}
		}

		// 카테고리 하나를 Content 에 그린다.
		// 그룹마다 제목을 놓고, 그리드형 그룹이면 GridSlot 하나를 깔아 그 안에 상품을 넣는다.
		public async UniTask RenderCategoryAsync(ShopCategory category, CancellationToken ct)
		{
			_bindTasks.Clear();
			resetCursors();

			int siblingCursor = 0;

			IReadOnlyList<Table_ShopGoodsGroup.Row> groups = ShopCatalog.GetGroups(category);
			for (int g = 0; g < groups.Count; g++)
			{
				IReadOnlyList<Table_ShopGoods.Row> goods = ShopCatalog.GetGoods(groups[g].ID);
				if (goods.Count == 0)
				{
					// 상품이 없는 그룹은 제목도 내지 않는다.
					continue;
				}

				ShopTitleSlot title = acquireTitle();
				place(title.transform, _content, siblingCursor);
				siblingCursor++;
				title.SetTitle(groups[g].Name);

				// 한 그룹의 상품은 표시 형태가 같다 — 첫 상품의 종류로 그룹 전체의 배치를 정한다.
				bool useGrid = isGridType(goods[0].GoodsType);
				RectTransform gridParent = null;
				if (useGrid == true)
				{
					gridParent = acquireGrid();
					place(gridParent, _content, siblingCursor);
					siblingCursor++;
				}

				int gridCursor = 0;
				for (int i = 0; i < goods.Count; i++)
				{
					ShopProductSlotBase slot = acquireSlot(goods[i].GoodsType);
					if (slot == null)
					{
						continue;
					}

					if (useGrid == true)
					{
						place(slot.transform, gridParent, gridCursor);
						gridCursor++;
					}
					else
					{
						place(slot.transform, _content, siblingCursor);
						siblingCursor++;
					}

					_bindTasks.Add(slot.BindAsync(goods[i], ct));
				}
			}

			deactivateUnused();

			// 아이콘이 전부 준비된 뒤 한 번에 보이게 한다 — 캐시 히트면 즉시 끝난다.
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();

			if (_scrollRect != null)
			{
				_scrollRect.verticalNormalizedPosition = 1f;
			}
		}

		// 구매 직후 그 상품의 남은 횟수 표시만 갱신한다.
		public void RefreshGoods(int goodsId)
		{
			Dictionary<GoodsType, SlotCache>.Enumerator e = _slotCaches.GetEnumerator();
			while (e.MoveNext() == true)
			{
				List<ShopProductSlotBase> slots = e.Current.Value.slots;
				for (int i = 0; i < slots.Count; i++)
				{
					ShopProductSlotBase slot = slots[i];
					if (slot.gameObject.activeSelf == true && slot.GoodsId == goodsId)
					{
						slot.RefreshPurchaseLimit();
					}
				}
			}
		}

		// ── 내부: 슬롯 재사용 ─────────────────────────────────────────────

		// 그리드(UIPrefab_ShopProductGridSlot)는 낱개 상품을 담는 SingleSlot 만 쓴다.
		// 나머지(패스·광고제거·패키지·상자)는 한 건이 화면 폭을 통째로 쓰므로 Content 에 직접 쌓는다.
		private static bool isGridType(GoodsType type)
		{
			return type == GoodsType.Currency || type == GoodsType.Item;
		}

		private void registerSlotPrefab(GoodsType type, ShopProductSlotBase prefab)
		{
			if (prefab == null || _slotCaches.ContainsKey(type) == true)
			{
				return;
			}

			SlotCache cache = new SlotCache();
			cache.prefab = prefab;
			_slotCaches.Add(type, cache);
		}

		private void resetCursors()
		{
			_titleCursor = 0;
			_gridCursor = 0;

			Dictionary<GoodsType, SlotCache>.Enumerator e = _slotCaches.GetEnumerator();
			while (e.MoveNext() == true)
			{
				e.Current.Value.cursor = 0;
			}
		}

		private ShopTitleSlot acquireTitle()
		{
			if (_titleCursor < _titleSlots.Count)
			{
				return _titleSlots[_titleCursor++];
			}

			ShopTitleSlot slot = Instantiate(_titlePrefab, _content);
			_titleSlots.Add(slot);
			_titleCursor++;
			return slot;
		}

		private RectTransform acquireGrid()
		{
			if (_gridCursor < _grids.Count)
			{
				return _grids[_gridCursor++];
			}

			RectTransform grid = Instantiate(_gridPrefab, _content);
			_grids.Add(grid);
			_gridCursor++;
			return grid;
		}

		private ShopProductSlotBase acquireSlot(GoodsType type)
		{
			SlotCache cache;
			if (_slotCaches.TryGetValue(type, out cache) == false)
			{
				Debug.LogWarning($"[Shop] {type} 에 대응하는 슬롯 프리펩이 없다 — 해당 상품을 건너뛴다.");
				return null;
			}

			if (cache.cursor < cache.slots.Count)
			{
				return cache.slots[cache.cursor++];
			}

			ShopProductSlotBase slot = Instantiate(cache.prefab, _content);
			slot.OnBuyClicked += onSlotBuyClicked;
			cache.slots.Add(slot);
			cache.cursor++;
			return slot;
		}

		// 세로 목록의 순서는 형제 순서가 정한다 — 재사용한 슬롯은 부모와 순서를 매번 다시 잡아준다.
		private static void place(Transform target, Transform parent, int siblingIndex)
		{
			if (target.parent != parent)
			{
				target.SetParent(parent, false);
			}

			target.SetSiblingIndex(siblingIndex);
			target.gameObject.SetActive(true);
		}

		private void deactivateUnused()
		{
			for (int i = _titleCursor; i < _titleSlots.Count; i++)
			{
				_titleSlots[i].gameObject.SetActive(false);
			}

			for (int i = _gridCursor; i < _grids.Count; i++)
			{
				_grids[i].gameObject.SetActive(false);
			}

			Dictionary<GoodsType, SlotCache>.Enumerator e = _slotCaches.GetEnumerator();
			while (e.MoveNext() == true)
			{
				SlotCache cache = e.Current.Value;
				for (int i = cache.cursor; i < cache.slots.Count; i++)
				{
					cache.slots[i].gameObject.SetActive(false);
				}
			}
		}

		// ── 내부: 입력 ────────────────────────────────────────────────────

		private void onTabChanged(int index)
		{
			if (OnTabChanged != null)
			{
				OnTabChanged.Invoke(index);
			}
		}

		private void onSlotBuyClicked(int goodsId)
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
