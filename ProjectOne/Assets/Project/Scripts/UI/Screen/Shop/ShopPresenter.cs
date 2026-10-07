using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Network;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.Shop;
using ProjectOne.UserData;
using UnityEngine;

namespace ProjectOne.UI
{
	// 상점 화면 Presenter — 어떤 카테고리를 그릴지 정하고 구매 입력을 받는다.
	//
	// 모든 상품은 서버(ShopBuy)가 구매 한도 판정·가격 차감·추첨·지급을 하고 결과를 내려준다.
	// 히어로패스는 보상 대신 패스 활성화가 효과라 응답 처리만 따로 둔다.
	public sealed class ShopPresenter : Presenter<ShopUI>
	{
		private IReadOnlyList<Table_ShopCategory.Row> _categories;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		// 응답을 기다리는 서버 구매 상품 — 0 이면 대기 중이 아니다. 응답 전 연타를 막는다.
		private int _pendingGoodsId;
		private int _pendingCount;	// 그 요청으로 산 개수 — 응답 후 가격·구매 횟수를 같은 수만큼 반영한다
		private bool _isDisposed;

		private const string INVENTORY_FULL_MESSAGE = "인벤토리가 가득 차서 상자를 열 수 없습니다.";

		protected override void OnInitialize()
		{
			view.OnTabChanged += onTabChanged;
			view.OnBuyClicked += onBuyClicked;
			view.OnHomeClicked += onHomeClicked;
		}

		protected override void OnDispose()
		{
			_isDisposed = true;

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnTabChanged -= onTabChanged;
			view.OnBuyClicked -= onBuyClicked;
			view.OnHomeClicked -= onHomeClicked;
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			_categories = ShopCatalog.GetCategories();
			view.SetCategories(_categories);

			if (_categories.Count == 0)
			{
				Debug.LogWarning("[Shop] 노출할 상점 카테고리가 없다 — ShopCategory 테이블을 확인한다.");
				return UniTask.CompletedTask;
			}

			view.SelectTab(0);
			render(0);

			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabChanged(int index)
		{
			render(index);
		}

		private void onBuyClicked(int goodsId)
		{
			Table_ShopGoods.Row row = Table_ShopGoods.Get(goodsId);
			if (row == null)
			{
				return;
			}

			if (ShopPurchaseCounter.CanPurchase(row) == false)
			{
				Debug.Log($"[Shop] 구매 한도 도달 goodsId={row.ID} name={row.Name}");
				return;
			}

			if (row.GoodsType == GoodsType.HeroPass)
			{
				requestHeroPass(row);
				return;
			}

			if (row.GoodsType == GoodsType.Box)
			{
				requestBoxOpen(row);
				return;
			}

			requestPurchase(row, 1, false);
		}

		// ── 보물상자 ──────────────────────────────────────────────────────

		// 상자는 묶음으로 연다. 자동분해를 체크했으면 보유한 만큼 전부 열고 나온 장비는 서버가 전부 분해한다.
		// 체크하지 않았으면 장비가 인벤토리로 들어오므로 빈칸만큼만 연다.
		private void requestBoxOpen(Table_ShopGoods.Row row)
		{
			bool decomposeAll = ShopBoxOpen.IsDecomposeChecked(row.ID);
			if (decomposeAll == false && ShopBoxOpen.GetFreeInventorySlots() <= 0)
			{
				UIManager.Instance.ShowAlertMessage(INVENTORY_FULL_MESSAGE);
				return;
			}

			int count = ShopBoxOpen.GetOpenCount(row, decomposeAll, false);
			if (count <= 0)
			{
				return;
			}

			requestPurchase(row, count, decomposeAll);
		}

		// ── 히어로패스 ────────────────────────────────────────────────────

		// 패스는 보상 그룹이 없다 — 구매 효과는 이번 시즌 패스 활성화다.
		// [임시] 서버가 결제 검증 없이 활성화한다(결제 단계에서 영수증 검증으로 교체).
		private void requestHeroPass(Table_ShopGoods.Row row)
		{
			if (_pendingGoodsId != 0 || NetworkManager.Instance.IsLoggedIn == false || Account.Instance.HeroPass.IsPurchased == true)
			{
				return;
			}

			ShopBuyRequest request = new ShopBuyRequest();
			request.goodsId = row.ID;
			_pendingGoodsId = row.ID;
			NetworkManager.Instance.RequestShopBuy(request, onHeroPassBought);
		}

		private void onHeroPassBought(bool success, ShopBuyResponse data, string error)
		{
			Table_ShopGoods.Row row = Table_ShopGoods.Get(_pendingGoodsId);
			_pendingGoodsId = 0;

			if (success == false || row == null)
			{
				Debug.LogWarning($"[Shop] 히어로패스 구매 실패: {error}");
				return;
			}

			Account.Instance.HeroPass.SetPurchased();

			if (_isDisposed == true)
			{
				return;
			}

			view.RefreshGoods(row.ID);
		}

		// ── 일반 상품(보물상자·패키지·재화 등) ─────────────────────────────

		private void requestPurchase(Table_ShopGoods.Row row, int count, bool decomposeAll)
		{
			if (_pendingGoodsId != 0)
			{
				return;
			}

			if (NetworkManager.Instance.IsLoggedIn == false)
			{
				Debug.LogWarning($"[Shop] 상품 구매는 서버 로그인 상태에서만 할 수 있다 goodsId={row.ID}");
				return;
			}

			if (hasEnoughPrice(row, count) == false)
			{
				return;
			}

			ShopBuyRequest request = new ShopBuyRequest();
			request.goodsId = row.ID;
			request.count = count;
			request.decomposeAll = decomposeAll;
			_pendingGoodsId = row.ID;
			_pendingCount = count;
			NetworkManager.Instance.RequestShopBuy(request, onPurchased);
		}

		// 계정 반영은 창이 닫혔어도 한다 — 서버에는 이미 저장됐다. 화면 갱신·결과 팝업만 창이 살아 있을 때 한다.
		private void onPurchased(bool success, ShopBuyResponse data, string error)
		{
			Table_ShopGoods.Row row = Table_ShopGoods.Get(_pendingGoodsId);
			int count = _pendingCount;
			_pendingGoodsId = 0;
			_pendingCount = 0;

			if (success == false || data == null || row == null)
			{
				Debug.LogWarning($"[Shop] 상품 구매 실패: {error}");
				return;
			}

			spendPrice(row, count);

			List<GrantedReward> granted = new List<GrantedReward>();
			RewardGranter.FromServer(data.rewards, data.equipments, granted);
			RewardGranter.ApplyAll(granted, false);

			for (int i = 0; i < count; i++)
			{
				ShopPurchaseCounter.Increase(row);
			}

			// 칸 확장 상품은 보상 대신 최대 칸 수가 늘어난다.
			if (row.GoodsType == GoodsType.InventorySlot)
			{
				Account.Instance.Inventory.AddInventoryCapacity(row.GoodsValue);
			}
			else if (row.GoodsType == GoodsType.StashSlot)
			{
				Account.Instance.Inventory.AddStashCapacity(row.GoodsValue);
			}

			if (_isDisposed == true)
			{
				return;
			}

			view.RefreshGoods(row.ID);

			if (granted.Count > 0)
			{
				UIManager.Instance.ShowRewardPopupAsync(granted, view.GetDestroyToken()).Forget();
			}
		}

		// 요청 전 사전 검사 — 최종 판정은 서버가 한다. 무료·광고·현금 상품은 로컬에서 볼 것이 없다.
		private static bool hasEnoughPrice(Table_ShopGoods.Row row, int count)
		{
			int price = row.Price * count;

			int itemId;
			if (ShopRules.TryGetItemPrice(row, out itemId) == true)
			{
				int owned = Account.Instance.Inventory.GetCount(itemId);
				if (owned < price)
				{
					Debug.LogWarning($"[Shop] 아이템 부족 goodsId={row.ID} item={itemId} 필요={price} 보유={owned}");
					return false;
				}

				return true;
			}

			EDT.Currency currency;
			if (ShopRules.TryGetCurrencyPrice(row, out currency) == true)
			{
				int owned = CurrencyManager.Instance.GetAmount(currency);
				if (owned < price)
				{
					Debug.LogWarning($"[Shop] 재화 부족 goodsId={row.ID} currency={currency} 필요={price} 보유={owned}");
					return false;
				}

				return true;
			}

			if (row.PriceType == PriceType.Item || row.PriceType == PriceType.Currency || row.PriceType == PriceType.None)
			{
				Debug.LogError($"[Shop] 가격 정보가 잘못됐다 goodsId={row.ID} priceType={row.PriceType} priceParam={row.PriceParam} price={row.Price}");
				return false;
			}

			return true;
		}

		// 서버가 차감을 확정한 가격을 로컬에도 증감으로 반영한다.
		private static void spendPrice(Table_ShopGoods.Row row, int count)
		{
			int price = row.Price * count;

			int itemId;
			if (ShopRules.TryGetItemPrice(row, out itemId) == true)
			{
				Account.Instance.Inventory.TrySpend(itemId, price);
				return;
			}

			EDT.Currency currency;
			if (ShopRules.TryGetCurrencyPrice(row, out currency) == true)
			{
				CurrencyManager.Instance.TrySpend(currency, price);
			}
		}

		// 창을 닫는다. 마지막 창이면 WindowClosedEvent 가 발행되어 네비게이션 바의 탭 선택도 함께 풀린다.
		private void onHomeClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 아이콘 로드가 await 라 탭을 빠르게 연타하면 렌더가 겹친다 — 직전 렌더는 취소한다.
		private void render(int categoryIndex)
		{
			if (_categories == null || categoryIndex < 0 || categoryIndex >= _categories.Count)
			{
				return;
			}

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderCategoryAsync(_categories[categoryIndex].ID, _renderCts.Token).Forget();
		}
	}
}
