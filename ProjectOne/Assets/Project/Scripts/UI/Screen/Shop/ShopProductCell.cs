using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.Shop;
using ProjectOne.UserData;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 상품 칸의 표시 형태. GoodsType 이 정한다.
	public enum ShopCellKind
	{
		Single,		// 재화·아이템·칸 확장 — 3열 그리드
		Box,		// 보물상자 — 열쇠로 연다
		Package,	// 여러 구성품 묶음
		Pass,		// 영웅 패스
		NoAd		// 광고 제거
	}

	// 상점 상품 1칸 (UI Toolkit). 스타일은 UIToolkit/Screens/Shop/ShopScreen.uss.
	//
	// 공통 — 이름, 상품 아이콘, 가격(아이콘+값), 구매 버튼, 남은 구매 횟수.
	// 종류별 — 낱개는 재화 수량 또는 아이템 칸, 상자는 설명·확률·자동분해, 패키지는 구성품 5열, 패스는 정보 버튼.
	public sealed class ShopProductCell
	{
		// 더 살 수 없는 상품의 가격 자리에 대신 넣는 문구.
		private const string SOLD_OUT_TEXT = "구매함";

		// 패키지 구성품 — 한 줄 5칸, 두 줄까지.
		private const int PACKAGE_MAX_DISPLAY = 10;

		private static readonly Color LackColor = new Color(1f, 0.35f, 0.35f);

		private readonly ShopCellKind _kind;
		private readonly ItemGradeColorTable _colors;
		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ownerToken;

		private readonly VisualElement _root;
		private readonly Label _title;
		private readonly VisualElement _goodsIcon;
		private readonly Label _limit;
		private readonly Button _buy;
		private readonly VisualElement _priceIcon;
		private readonly Label _price;

		// 낱개
		private VisualElement _media;
		private Label _amount;
		private Label _bonus;
		private ItemCellView _itemCell;
		private RewardPreviewItem _singleItem;

		// 상자
		private Label _desc;
		private VisualElement _decomposeMark;

		// 패키지
		private VisualElement _packageGrid;
		private readonly List<ItemCellView> _packageCells = new List<ItemCellView>();
		private readonly List<RewardPreviewItem> _packageItems = new List<RewardPreviewItem>();

		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>();
		private readonly List<UniTask> _tasks = new List<UniTask>();

		private Table_ShopGoods.Row _row;

		public event Action<int> OnBuyClicked;

		public ShopCellKind Kind
		{
			get { return _kind; }
		}

		public int GoodsId
		{
			get { return _row != null ? _row.ID : 0; }
		}

		public VisualElement Root
		{
			get { return _root; }
		}

		public ShopProductCell(ShopCellKind kind, ItemGradeColorTable colors, ToolkitIconLoader icons, CancellationToken ownerToken)
		{
			_kind = kind;
			_colors = colors;
			_icons = icons;
			_ownerToken = ownerToken;

			_root = new VisualElement();
			_root.AddToClassList("px-paper");
			_root.AddToClassList("shop-card");
			_root.AddToClassList(kind == ShopCellKind.Single ? "shop-card--single" : "shop-card--wide");

			_title = new Label();
			_title.AddToClassList("shop-card__title");

			_goodsIcon = new VisualElement();
			_goodsIcon.AddToClassList("shop-card__goods-icon");

			_limit = new Label();
			_limit.AddToClassList("shop-card__limit");

			_buy = new Button();
			_buy.AddToClassList("btn");
			_buy.AddToClassList("btn--green");
			_buy.AddToClassList("shop-card__buy");
			_priceIcon = new VisualElement();
			_priceIcon.AddToClassList("shop-card__price-icon");
			_priceIcon.pickingMode = PickingMode.Ignore;
			_price = new Label();
			_price.AddToClassList("shop-card__price");
			_price.pickingMode = PickingMode.Ignore;
			_buy.Add(_priceIcon);
			_buy.Add(_price);
			_buy.clicked += onBuyClicked;

			if (kind == ShopCellKind.Single)
			{
				buildSingle();
			}
			else
			{
				buildWide();
			}
		}

		// ── 구성 ─────────────────────────────────────────────────────────

		private void buildSingle()
		{
			_root.Add(_title);

			_media = new VisualElement();
			_media.AddToClassList("shop-card__media");
			_goodsIcon.AddToClassList("shop-card__currency");
			_media.Add(_goodsIcon);
			_itemCell = new ItemCellView(_media, _colors, _icons);
			_itemCell.Root.AddToClassList("shop-card__item");
			_itemCell.OnClicked += onSingleItemClicked;
			_root.Add(_media);

			_amount = new Label();
			_amount.AddToClassList("shop-card__amount");
			_root.Add(_amount);

			_bonus = new Label();
			_bonus.AddToClassList("chip");
			_bonus.AddToClassList("chip--core");
			_bonus.AddToClassList("shop-card__bonus");
			_root.Add(_bonus);

			_root.Add(_limit);
			_root.Add(_buy);
		}

		private void buildWide()
		{
			VisualElement body = new VisualElement();
			body.AddToClassList("shop-card__row");

			if (_kind != ShopCellKind.Package)
			{
				VisualElement frame = new VisualElement();
				frame.AddToClassList("shop-card__icon-frame");
				_goodsIcon.AddToClassList("icon");
				frame.Add(_goodsIcon);
				body.Add(frame);
			}

			VisualElement text = new VisualElement();
			text.AddToClassList("shop-card__text");
			text.Add(_title);

			if (_kind == ShopCellKind.Box || _kind == ShopCellKind.NoAd)
			{
				_desc = new Label();
				_desc.AddToClassList("shop-card__desc");
				text.Add(_desc);
			}

			text.Add(_limit);
			body.Add(text);
			_root.Add(body);

			if (_kind == ShopCellKind.Package)
			{
				_packageGrid = new VisualElement();
				_packageGrid.AddToClassList("shop-card__package");
				_root.Add(_packageGrid);
			}

			VisualElement buttons = new VisualElement();
			buttons.AddToClassList("shop-card__buttons");

			if (_kind == ShopCellKind.Box)
			{
				Button decompose = new Button();
				decompose.AddToClassList("shop-card__check");
				_decomposeMark = new VisualElement();
				_decomposeMark.AddToClassList("shop-card__check-box");
				_decomposeMark.pickingMode = PickingMode.Ignore;
				Label checkLabel = new Label("자동분해");
				checkLabel.pickingMode = PickingMode.Ignore;
				decompose.Add(_decomposeMark);
				decompose.Add(checkLabel);
				decompose.clicked += onDecomposeClicked;
				buttons.Add(decompose);

				Button info = new Button();
				info.text = "확률";
				info.AddToClassList("btn");
				info.AddToClassList("btn--small");
				info.clicked += onBoxInfoClicked;
				buttons.Add(info);
			}
			else if (_kind == ShopCellKind.Pass)
			{
				Button info = new Button();
				info.text = "정보";
				info.AddToClassList("btn");
				info.AddToClassList("btn--small");
				info.clicked += onPassInfoClicked;
				buttons.Add(info);
			}

			buttons.Add(_buy);
			_root.Add(buttons);
		}

		// ── 바인드 ───────────────────────────────────────────────────────

		public async UniTask BindAsync(Table_ShopGoods.Row row, CancellationToken ct)
		{
			_row = row;
			if (row == null)
			{
				return;
			}

			_title.text = row.Name;
			if (_desc != null)
			{
				_desc.text = row.Desc;
			}

			applyPurchaseLimit();

			_tasks.Clear();
			_tasks.Add(_icons.SetAsync(_priceIcon, getPriceIconAddress(), ct));

			switch (_kind)
			{
				case ShopCellKind.Single:
					_tasks.Add(bindSingleAsync(ct));
					break;

				case ShopCellKind.Package:
					_tasks.Add(bindPackageAsync(ct));
					break;

				default:
					_tasks.Add(_icons.SetAsync(_goodsIcon, row.Icon, ct));
					break;
			}

			await UniTask.WhenAll(_tasks).SuppressCancellationThrow();
		}

		// 구매 직후 남은 횟수 표시만 다시 계산한다.
		public void RefreshPurchaseLimit()
		{
			if (_row != null)
			{
				applyPurchaseLimit();
			}
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 낱개 — 재화·칸 확장은 상품 이미지와 수량, 아이템은 아이템 칸 하나.
		private UniTask bindSingleAsync(CancellationToken ct)
		{
			bool hasBonus = string.IsNullOrEmpty(_row.Bonus) == false;
			setShown(_bonus, hasBonus);
			_bonus.text = hasBonus ? _row.Bonus : string.Empty;

			// 칸 확장 상품은 보상 그룹이 없다 — 이미지와 늘어나는 칸 수만 보여준다.
			if (_row.GoodsType == GoodsType.InventorySlot || _row.GoodsType == GoodsType.StashSlot)
			{
				_singleItem = default(RewardPreviewItem);
				showCurrency("+" + _row.GoodsValue.ToString(CultureInfo.InvariantCulture));
				return _icons.SetAsync(_goodsIcon, _row.Icon, ct);
			}

			_preview.Clear();
			RewardPreview.Build(_row.RewardGroupID, _preview);
			if (_preview.Count == 0)
			{
				_singleItem = default(RewardPreviewItem);
				setShown(_goodsIcon, false);
				setShown(_amount, false);
				_itemCell.SetShown(false);
				return UniTask.CompletedTask;
			}

			_singleItem = _preview[0];
			if (_singleItem.equipment == null && _singleItem.type == RewardType.Currency)
			{
				showCurrency(_singleItem.count.ToString("N0", CultureInfo.InvariantCulture));
				return _icons.SetAsync(_goodsIcon, _row.Icon, ct);
			}

			setShown(_goodsIcon, false);
			setShown(_amount, false);
			_itemCell.SetShown(true);
			return bindCell(_itemCell, _singleItem, ct);
		}

		private void showCurrency(string amount)
		{
			_itemCell.SetShown(false);
			setShown(_goodsIcon, true);
			setShown(_amount, true);
			_amount.text = amount;
		}

		private UniTask bindPackageAsync(CancellationToken ct)
		{
			_preview.Clear();
			_packageItems.Clear();
			RewardPreview.Build(_row.RewardGroupID, _preview);

			int count = _preview.Count < PACKAGE_MAX_DISPLAY ? _preview.Count : PACKAGE_MAX_DISPLAY;
			List<UniTask> tasks = new List<UniTask>(count);
			for (int i = 0; i < count; i++)
			{
				ItemCellView cell = getPackageCell(i);
				cell.SetShown(true);
				cell.Tag = i;
				_packageItems.Add(_preview[i]);
				tasks.Add(bindCell(cell, _preview[i], ct));
			}

			for (int i = count; i < _packageCells.Count; i++)
			{
				_packageCells[i].SetShown(false);
			}

			return UniTask.WhenAll(tasks);
		}

		private ItemCellView getPackageCell(int index)
		{
			if (index < _packageCells.Count)
			{
				return _packageCells[index];
			}

			ItemCellView cell = new ItemCellView(_packageGrid, _colors, _icons);
			cell.OnClicked += onPackageItemClicked;
			_packageCells.Add(cell);
			return cell;
		}

		private static UniTask bindCell(ItemCellView cell, RewardPreviewItem item, CancellationToken ct)
		{
			if (item.equipment != null)
			{
				return cell.BindEquipmentAsync(item.equipment, false, ct);
			}

			if (item.type == RewardType.Currency)
			{
				return cell.BindCurrencyAsync(item.currency, item.count, ct);
			}

			Table_Item.Row itemRow = Table_Item.Get(item.itemId);
			if (itemRow == null)
			{
				cell.SetShown(false);
				return UniTask.CompletedTask;
			}

			return cell.BindItemAsync(itemRow, item.count, ct);
		}

		// ── 가격 · 구매 제한 ─────────────────────────────────────────────

		// PriceParam 은 PriceType 이 해석 방법을 정한다 — 재화면 enum 이름, 아이템이면 아이템 ID.
		private string getPriceIconAddress()
		{
			switch (_row.PriceType)
			{
				case PriceType.Currency:
				{
					EDT.Currency currency;
					if (Enum.TryParse<EDT.Currency>(_row.PriceParam, false, out currency) == false)
					{
						return string.Empty;
					}

					Table_Currency.Row currencyRow = Table_Currency.Get(currency);
					return currencyRow != null ? currencyRow.Icon : string.Empty;
				}

				case PriceType.Item:
				{
					int itemId;
					if (int.TryParse(_row.PriceParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) == false)
					{
						return string.Empty;
					}

					Table_Item.Row itemRow = Table_Item.Get(itemId);
					return itemRow != null ? itemRow.Icon : string.Empty;
				}
			}

			// 현금·광고·무료는 아이콘 없이 글자로만 표시한다.
			return string.Empty;
		}

		private string getPriceText()
		{
			switch (_row.PriceType)
			{
				case PriceType.Currency:
				case PriceType.Item:
					return _row.Price.ToString("N0", CultureInfo.InvariantCulture);

				case PriceType.Cash:
					return $"₩{_row.Price.ToString("N0", CultureInfo.InvariantCulture)}";

				case PriceType.Ad:
					return "광고 보기";

				case PriceType.Free:
					return "무료";
			}

			return string.Empty;
		}

		private void applyPurchaseLimit()
		{
			int remaining = ShopPurchaseCounter.GetRemaining(_row);
			bool unlimited = remaining == ShopPurchaseCounter.UNLIMITED;

			_price.text = getPriceText();
			_price.style.color = StyleKeyword.Null;
			setShown(_priceIcon, string.IsNullOrEmpty(getPriceIconAddress()) == false);

			// 하루마다 되살아나지 않는 상품이 소진되면 가격 대신 구매 완료를 알린다.
			if (remaining == 0 && _row.UseDailyReset == false)
			{
				_price.text = SOLD_OUT_TEXT;
				setShown(_priceIcon, false);
			}

			// 수집품(펫·코스튬)은 보유하면 끝이라 횟수 표시가 의미 없다.
			int collectibleId;
			bool isCollectible = ShopRules.TryGetCollectible(_row, out collectibleId);
			if (unlimited == true || isCollectible == true)
			{
				setShown(_limit, false);
			}
			else
			{
				setShown(_limit, true);
				_limit.text = $"구매 {remaining}/{_row.MaxPurchaseCount}";
				_limit.style.color = remaining > 0 ? new StyleColor(StyleKeyword.Null) : new StyleColor(LackColor);
			}

			_buy.SetEnabled(unlimited == true || remaining > 0);

			if (_kind == ShopCellKind.Box)
			{
				applyBoxPrice();
			}
		}

		// 상자 — 가격 자리에 이번에 열 상자 수를 보여준다. 열쇠가 모자라면 필요 수를 빨갛게 띄우고 버튼을 잠근다.
		private void applyBoxPrice()
		{
			bool decomposeAll = ShopBoxOpen.IsDecomposeChecked(_row.ID);
			_decomposeMark.EnableInClassList("shop-card__check-box--on", decomposeAll);

			if (_row.PriceType != PriceType.Item)
			{
				return;
			}

			int itemId;
			if (int.TryParse(_row.PriceParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) == false)
			{
				return;
			}

			bool noSpace = decomposeAll == false && ShopBoxOpen.GetFreeInventorySlots() <= 0;
			int openCount = ShopBoxOpen.GetOpenCount(_row, decomposeAll, noSpace);

			Inventory inventory = Account.Instance.Inventory;
			int owned = (inventory != null) ? inventory.GetCount(itemId) : 0;
			bool enough = owned >= _row.Price;

			_price.text = (enough ? openCount : _row.Price).ToString("N0", CultureInfo.InvariantCulture);
			_price.style.color = enough ? new StyleColor(StyleKeyword.Null) : new StyleColor(LackColor);
			if (enough == false)
			{
				_buy.SetEnabled(false);
			}
		}

		// ── 입력 ─────────────────────────────────────────────────────────

		private void onBuyClicked()
		{
			if (_row != null && OnBuyClicked != null)
			{
				OnBuyClicked.Invoke(_row.ID);
			}
		}

		// 진열된 것은 내 것이 아니므로 읽기 전용 팝업만 연다.
		private void onSingleItemClicked(ItemCellView sender, long uid, int itemId)
		{
			ShopRewardPopup.Show(_singleItem, sender.Root, _ownerToken);
		}

		private void onPackageItemClicked(ItemCellView sender, long uid, int itemId)
		{
			int index = (sender.Tag is int) ? (int)sender.Tag : -1;
			if (index < 0 || index >= _packageItems.Count)
			{
				return;
			}

			ShopRewardPopup.Show(_packageItems[index], sender.Root, _ownerToken);
		}

		private void onDecomposeClicked()
		{
			if (_row == null)
			{
				return;
			}

			ShopBoxOpen.SetDecomposeChecked(_row.ID, ShopBoxOpen.IsDecomposeChecked(_row.ID) == false);
			applyPurchaseLimit();
		}

		// 이 상자에서 무엇이 얼마나 나오는지 확률표를 연다.
		private void onBoxInfoClicked()
		{
			if (_row == null || _row.RewardGroupID <= 0)
			{
				Debug.LogWarning($"[Shop] 보상 그룹이 없어 확률표를 열 수 없다 — goodsId={(_row != null ? _row.ID : 0)}");
				return;
			}

			UIManager.Instance.ShowBoxRewardPopupAsync(_row.RewardGroupID, _ownerToken).Forget();
		}

		private void onPassInfoClicked()
		{
			UIManager.Instance.ShowHeroPassPopupAsync(_ownerToken).Forget();
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
