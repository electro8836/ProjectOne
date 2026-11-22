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
		Single,		// 재화·아이템 — 3열 칸
		Box,		// 보물상자 — 열쇠로 연다
		Package,	// 여러 구성품 묶음
		Pass,		// 영웅 패스 — 주력 상품(높은 카드 + 배너 그림)
		NoAd,		// 광고 제거
		Expand		// 인벤토리·창고 칸 확장
	}

	// 상점 상품 1칸 (UI Toolkit). 스타일은 UIToolkit/Screens/Shop/ShopScreen.uss, 부품 그림은 UIToolkit/Shop.
	//
	// 공통 — 검정 테 + 흰 테 + 색 바탕 카드, 이름, 맨 아래 가운데 구매 버튼(가격 아이콘 + 값).
	// 종류별 — 낱개는 그림·수량·가치 말풍선, 가로 카드(상자·광고 제거·칸 확장)는 왼쪽 그림 + 설명,
	// 패스는 배너 그림 + 돋보기(패스 정보), 패키지는 구성 칸(누르면 아이템 정보), 상자는 돋보기(확률) + 자동분해.
	// 남은 구매 횟수는 오른쪽 위 알약, 리본(1회 한정 · 최고 인기 · 매일 1회)은 왼쪽 위.
	public sealed class ShopProductCell
	{
		// 더 살 수 없는 상품의 가격 자리에 대신 넣는 문구.
		private const string SOLD_OUT_TEXT = "구매함";

		// 패키지 구성품 — 한 줄에 보이는 칸 수.
		private const int PACKAGE_MAX_DISPLAY = 6;

		// 구성품이 비어 있는 패키지의 대표 그림(테이블 Icon 이 비었을 때).
		private const string PACKAGE_DEFAULT_ICON = "ItemIcon_Chest_02";

		private static readonly Color LackColor = new Color(1f, 0.35f, 0.35f);

		private readonly ShopCellKind _kind;
		private readonly ItemGradeColorTable _colors;
		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ownerToken;

		private readonly VisualElement _root;
		private readonly Label _title;
		private readonly VisualElement _goodsIcon;
		private readonly Label _limit;
		private readonly Label _ribbon;
		private readonly Button _buy;
		private readonly VisualElement _priceIcon;
		private readonly Label _price;
		private string _colorClass;

		// 낱개
		private VisualElement _media;
		private Label _amount;
		private VisualElement _bonus;
		private Label _bonusText;
		private ItemCellView _itemCell;
		private RewardPreviewItem _singleItem;

		// 가로 카드 · 패스
		private Label _desc;
		private VisualElement _decomposeMark;

		// 패키지
		private VisualElement _packageGrid;
		private VisualElement _packageSubA;
		private VisualElement _packageSubB;
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

		// 가치 말풍선(낱개 칸만) — 강조 연출은 ShopUI 가 돌린다. 없거나 숨겨졌으면 null.
		public VisualElement Bonus
		{
			get { return (_bonus != null && _bonus.resolvedStyle.display == DisplayStyle.Flex) ? _bonus : null; }
		}

		public ShopProductCell(ShopCellKind kind, ItemGradeColorTable colors, ToolkitIconLoader icons, CancellationToken ownerToken)
		{
			_kind = kind;
			_colors = colors;
			_icons = icons;
			_ownerToken = ownerToken;

			_root = new VisualElement();
			_root.AddToClassList("shop-card");
			_root.AddToClassList(getKindClass(kind));

			VisualElement face = new VisualElement();
			face.AddToClassList("shop-card__face");
			face.pickingMode = PickingMode.Ignore;
			_root.Add(face);

			_title = new Label();
			_title.AddToClassList("shop-card__title");
			_title.pickingMode = PickingMode.Ignore;

			_goodsIcon = new VisualElement();
			_goodsIcon.pickingMode = PickingMode.Ignore;

			_limit = new Label();
			_limit.AddToClassList("shop-count");
			_limit.pickingMode = PickingMode.Ignore;

			_ribbon = new Label();
			_ribbon.AddToClassList("shop-ribbon");
			_ribbon.pickingMode = PickingMode.Ignore;

			_buy = new Button();
			_buy.AddToClassList("shop-buy");
			_priceIcon = new VisualElement();
			_priceIcon.AddToClassList("shop-buy__icon");
			_priceIcon.pickingMode = PickingMode.Ignore;
			_price = new Label();
			_price.AddToClassList("shop-buy__price");
			_price.pickingMode = PickingMode.Ignore;
			_buy.Add(_priceIcon);
			_buy.Add(_price);
			_buy.clicked += onBuyClicked;

			switch (kind)
			{
				case ShopCellKind.Single:
					buildSingle();
					break;

				case ShopCellKind.Pass:
					buildPass();
					break;

				case ShopCellKind.Package:
					buildPackage();
					break;

				default:
					buildRow();
					break;
			}

			// 겹쳐 그려지는 것은 마지막에 — 구매 버튼 · 구매 횟수 · 리본
			_root.Add(_buy);
			_root.Add(_limit);
			_root.Add(_ribbon);
		}

		private static string getKindClass(ShopCellKind kind)
		{
			switch (kind)
			{
				case ShopCellKind.Single:
					return "shop-card--tile";

				case ShopCellKind.Pass:
					return "shop-card--pass";

				case ShopCellKind.Package:
					return "shop-card--package";
			}

			return "shop-card--row";
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
			_amount.pickingMode = PickingMode.Ignore;
			_root.Add(_amount);

			// 가치 말풍선 — 몸통(글자 + 빛 띠) + 꼬리
			_bonus = new VisualElement();
			_bonus.AddToClassList("shop-bubble");
			_bonus.pickingMode = PickingMode.Ignore;
			VisualElement body = new VisualElement();
			body.AddToClassList("shop-bubble__body");
			body.pickingMode = PickingMode.Ignore;
			_bonusText = new Label();
			_bonusText.AddToClassList("shop-bubble__text");
			_bonusText.pickingMode = PickingMode.Ignore;
			VisualElement shine = new VisualElement();
			shine.AddToClassList("shop-bubble__shine");
			shine.pickingMode = PickingMode.Ignore;
			body.Add(_bonusText);
			body.Add(shine);
			VisualElement tail = new VisualElement();
			tail.AddToClassList("shop-bubble__tail");
			tail.pickingMode = PickingMode.Ignore;
			_bonus.Add(body);
			_bonus.Add(tail);
			_root.Add(_bonus);
		}

		// 상자 · 광고 제거 · 칸 확장 — 왼쪽 그림, 오른쪽 이름 · 설명(상자는 자동분해 체크), 상자는 돋보기(확률).
		private void buildRow()
		{
			_goodsIcon.AddToClassList("shop-card__art");
			_root.Add(_goodsIcon);
			_root.Add(_title);

			_desc = new Label();
			_desc.AddToClassList("shop-card__desc");
			_desc.pickingMode = PickingMode.Ignore;
			_root.Add(_desc);

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
				_root.Add(decompose);

				addInfoButton(onBoxInfoClicked);
			}
		}

		// 패스 — 위 배너 그림(로고 + 보상 부채꼴), 이름 · 설명, 돋보기(패스 정보).
		private void buildPass()
		{
			VisualElement banner = new VisualElement();
			banner.AddToClassList("shop-card__banner");
			banner.pickingMode = PickingMode.Ignore;
			_root.Add(banner);
			_root.Add(_title);

			_desc = new Label();
			_desc.AddToClassList("shop-card__desc");
			_desc.pickingMode = PickingMode.Ignore;
			_root.Add(_desc);

			addInfoButton(onPassInfoClicked);
		}

		// 패키지 — 왼쪽 위 대표 그림(+ 들어 있는 재화 둘), 이름 · 구성 수, 구성 칸 한 줄.
		private void buildPackage()
		{
			VisualElement art = new VisualElement();
			art.AddToClassList("shop-card__package-art");
			art.pickingMode = PickingMode.Ignore;
			_goodsIcon.AddToClassList("shop-card__package-main");
			_packageSubA = new VisualElement();
			_packageSubA.AddToClassList("shop-card__package-sub");
			_packageSubA.AddToClassList("shop-card__package-sub--a");
			_packageSubA.pickingMode = PickingMode.Ignore;
			_packageSubB = new VisualElement();
			_packageSubB.AddToClassList("shop-card__package-sub");
			_packageSubB.AddToClassList("shop-card__package-sub--b");
			_packageSubB.pickingMode = PickingMode.Ignore;
			art.Add(_goodsIcon);
			art.Add(_packageSubA);
			art.Add(_packageSubB);
			_root.Add(art);
			_root.Add(_title);

			_desc = new Label();
			_desc.AddToClassList("shop-card__desc");
			_desc.pickingMode = PickingMode.Ignore;
			_root.Add(_desc);

			_packageGrid = new VisualElement();
			_packageGrid.AddToClassList("shop-card__package");
			_root.Add(_packageGrid);
		}

		private void addInfoButton(Action handler)
		{
			Button info = new Button();
			info.AddToClassList("shop-info");
			info.clicked += handler;
			_root.Add(info);
			_root.AddToClassList("shop-card--has-info");
		}

		// ── 바인드 ───────────────────────────────────────────────────────

		// 카드 바탕 색 — ShopUI 가 분류·순서로 정한다(낱개 재화는 재화 종류가 덮어쓴다).
		public void SetColor(string colorKey)
		{
			string next = "shop-card--c-" + colorKey;
			if (_colorClass == next)
			{
				return;
			}

			if (_colorClass != null)
			{
				_root.RemoveFromClassList(_colorClass);
			}

			_colorClass = next;
			_root.AddToClassList(next);
		}

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

			applyRibbon();
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

				case ShopCellKind.Pass:
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

		// 리본 — 패스는 주력 표시, 한 번만 살 수 있는 패키지는 1회 한정, 매일 되살아나는 낱개는 매일 1회.
		private void applyRibbon()
		{
			string text = null;
			bool violet = false;

			if (_kind == ShopCellKind.Pass)
			{
				text = "최고 인기";
			}
			else if (_kind == ShopCellKind.Package && _row.MaxPurchaseCount == 1 && _row.UseDailyReset == false)
			{
				text = "1회 한정";
			}
			else if (_kind == ShopCellKind.Single && _row.UseDailyReset == true)
			{
				text = "매일 1회";
				violet = true;
			}

			setShown(_ribbon, text != null);
			_ribbon.text = text ?? string.Empty;
			_ribbon.EnableInClassList("shop-ribbon--violet", violet);
		}

		// 낱개 — 재화는 상품 그림과 수량, 아이템은 아이템 칸 하나. 보너스가 있으면 가치 말풍선.
		private UniTask bindSingleAsync(CancellationToken ct)
		{
			bool hasBonus = string.IsNullOrEmpty(_row.Bonus) == false;
			setShown(_bonus, hasBonus);
			_bonusText.text = hasBonus ? "보너스 +" + _row.Bonus : string.Empty;

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
				SetColor(getCurrencyColor(_singleItem.currency));
				_itemCell.SetShown(false);
				setShown(_goodsIcon, true);
				setShown(_amount, true);
				_amount.text = _singleItem.count.ToString("N0", CultureInfo.InvariantCulture);
				return _icons.SetAsync(_goodsIcon, getCurrencyArt(_singleItem.currency), ct);
			}

			setShown(_goodsIcon, false);
			setShown(_amount, false);
			_itemCell.SetShown(true);
			return bindCell(_itemCell, _singleItem, ct);
		}

		// 재화 상품 그림 — 테이블 Icon, 비어 있으면 그 재화 아이콘.
		private string getCurrencyArt(EDT.Currency currency)
		{
			if (string.IsNullOrEmpty(_row.Icon) == false)
			{
				return _row.Icon;
			}

			Table_Currency.Row currencyRow = Table_Currency.Get(currency);
			return currencyRow != null ? currencyRow.Icon : string.Empty;
		}

		private static string getCurrencyColor(EDT.Currency currency)
		{
			switch (currency)
			{
				case EDT.Currency.Dia:
					return "blue";

				case EDT.Currency.Gold:
					return "yellow";

				case EDT.Currency.Ruby:
					return "red";
			}

			return "purple";
		}

		private UniTask bindPackageAsync(CancellationToken ct)
		{
			_preview.Clear();
			_packageItems.Clear();
			RewardPreview.Build(_row.RewardGroupID, _preview);

			_desc.text = _preview.Count + "종 구성";

			int count = _preview.Count < PACKAGE_MAX_DISPLAY ? _preview.Count : PACKAGE_MAX_DISPLAY;
			List<UniTask> tasks = new List<UniTask>(count + 3);
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

			// 대표 그림 — 테이블 Icon, 없으면 선물 상자. 옆에 들어 있는 재화 그림을 둘까지 곁들인다.
			string mainIcon = string.IsNullOrEmpty(_row.Icon) ? PACKAGE_DEFAULT_ICON : _row.Icon;
			tasks.Add(_icons.SetAsync(_goodsIcon, mainIcon, ct));

			int sub = 0;
			for (int i = 0; i < _preview.Count && sub < 2; i++)
			{
				if (_preview[i].equipment != null || _preview[i].type != RewardType.Currency)
				{
					continue;
				}

				Table_Currency.Row currencyRow = Table_Currency.Get(_preview[i].currency);
				if (currencyRow == null)
				{
					continue;
				}

				VisualElement target = (sub == 0) ? _packageSubA : _packageSubB;
				setShown(target, true);
				tasks.Add(_icons.SetAsync(target, currencyRow.Icon, ct));
				sub++;
			}

			if (sub < 1)
			{
				setShown(_packageSubA, false);
			}

			if (sub < 2)
			{
				setShown(_packageSubB, false);
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

		// 버튼 색 — 현금은 초록, 재화·아이템은 금색, 무료·광고는 보라.
		private void applyBuyStyle()
		{
			bool gold = _row.PriceType == PriceType.Currency || _row.PriceType == PriceType.Item;
			bool violet = _row.PriceType == PriceType.Free || _row.PriceType == PriceType.Ad;
			_buy.EnableInClassList("shop-buy--gold", gold);
			_buy.EnableInClassList("shop-buy--violet", violet);
		}

		private void applyPurchaseLimit()
		{
			int remaining = ShopPurchaseCounter.GetRemaining(_row);
			bool unlimited = remaining == ShopPurchaseCounter.UNLIMITED;

			applyBuyStyle();
			_price.text = getPriceText();
			_price.style.color = StyleKeyword.Null;
			setShown(_priceIcon, string.IsNullOrEmpty(getPriceIconAddress()) == false);

			// 하루마다 되살아나지 않는 상품이 소진되면 가격 대신 구매 완료를 알린다.
			if (remaining == 0 && _row.UseDailyReset == false)
			{
				_price.text = SOLD_OUT_TEXT;
				setShown(_priceIcon, false);
			}

			// 수집품(펫·코스튬)은 보유하면 끝이라 횟수 표시가 의미 없다. 낱개 칸은 리본 · 버튼 문구로 충분해 알약을 두지 않는다.
			int collectibleId;
			bool isCollectible = ShopRules.TryGetCollectible(_row, out collectibleId);
			if (unlimited == true || isCollectible == true || _kind == ShopCellKind.Single)
			{
				setShown(_limit, false);
			}
			else
			{
				setShown(_limit, true);
				_limit.text = $"구매 {remaining}/{_row.MaxPurchaseCount}";
				_limit.EnableInClassList("shop-count--lack", remaining <= 0);
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
