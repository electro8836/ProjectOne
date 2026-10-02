using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
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
	// 보물상자(GoodsType.Box)는 서버(ShopBuy)가 열쇠 차감·추첨·지급을 하고 결과를 내려준다.
	// 다른 상품은 아직 서버에 나가지 않는다 — 로그를 남기고 구매 횟수만 올린다.
	public sealed class ShopPresenter : Presenter<ShopUI>
	{
		private IReadOnlyList<Table_ShopCategory.Row> _categories;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		// 응답을 기다리는 서버 구매 상품(상자·히어로패스) — 0 이면 대기 중이 아니다. 응답 전 연타를 막는다.
		private int _pendingBoxGoodsId;
		private bool _isDisposed;

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

			if (row.GoodsType == GoodsType.Box)
			{
				requestBox(row);
				return;
			}

			if (row.GoodsType == GoodsType.HeroPass)
			{
				requestHeroPass(row);
				return;
			}

			// TODO(서버) — 실제 구매 요청으로 교체한다. 결제 검증·재화 차감·보상 지급 모두 서버 권위여야 한다.
			Debug.Log($"[Shop] 구매 요청 goodsId={row.ID} name={row.Name} goodsType={row.GoodsType} priceType={row.PriceType} price={row.Price} priceParam={row.PriceParam} rewardGroupId={row.RewardGroupID}");

			ShopPurchaseCounter.Increase(row.ID, row.UseDailyReset);
			view.RefreshGoods(row.ID);
		}

		// ── 히어로패스 ────────────────────────────────────────────────────

		// 패스는 보상 그룹이 없다 — 구매 효과는 이번 시즌 패스 활성화다.
		// [임시] 서버가 결제 검증 없이 활성화한다(결제 단계에서 영수증 검증으로 교체).
		private void requestHeroPass(Table_ShopGoods.Row row)
		{
			if (_pendingBoxGoodsId != 0 || NetworkManager.Instance.IsLoggedIn == false || Account.Instance.HeroPass.IsPurchased == true)
			{
				return;
			}

			ShopBuyRequest request = new ShopBuyRequest();
			request.goodsId = row.ID;
			_pendingBoxGoodsId = row.ID;
			NetworkManager.Instance.RequestShopBuy(request, onHeroPassBought);
		}

		private void onHeroPassBought(bool success, ShopBuyResponse data, string error)
		{
			Table_ShopGoods.Row row = Table_ShopGoods.Get(_pendingBoxGoodsId);
			_pendingBoxGoodsId = 0;

			if (success == false || row == null)
			{
				Debug.LogWarning($"[Shop] 히어로패스 구매 실패: {error}");
				return;
			}

			Account.Instance.HeroPass.SetPurchased();
			ShopPurchaseCounter.Increase(row.ID, row.UseDailyReset);

			if (_isDisposed == true)
			{
				return;
			}

			view.RefreshGoods(row.ID);
		}

		// ── 보물상자 ──────────────────────────────────────────────────────

		private void requestBox(Table_ShopGoods.Row row)
		{
			if (_pendingBoxGoodsId != 0)
			{
				return;
			}

			if (NetworkManager.Instance.IsLoggedIn == false)
			{
				Debug.LogWarning($"[Shop] 보물상자는 서버 로그인 상태에서만 열 수 있다 goodsId={row.ID}");
				return;
			}

			int keyItemId;
			if (tryGetKeyItemId(row, out keyItemId) == false)
			{
				Debug.LogError($"[Shop] 보물상자 가격이 열쇠 아이템이 아니다 goodsId={row.ID} priceType={row.PriceType} priceParam={row.PriceParam}");
				return;
			}

			if (Account.Instance.Inventory.GetCount(keyItemId) < row.Price)
			{
				Debug.LogWarning($"[Shop] 열쇠 부족 goodsId={row.ID} key={keyItemId} 필요={row.Price} 보유={Account.Instance.Inventory.GetCount(keyItemId)}");
				return;
			}

			ShopBuyRequest request = new ShopBuyRequest();
			request.goodsId = row.ID;
			_pendingBoxGoodsId = row.ID;
			NetworkManager.Instance.RequestShopBuy(request, onBoxOpened);
		}

		// 계정 반영은 창이 닫혔어도 한다 — 서버에는 이미 저장됐다. 화면 갱신·결과 팝업만 창이 살아 있을 때 한다.
		private void onBoxOpened(bool success, ShopBuyResponse data, string error)
		{
			Table_ShopGoods.Row row = Table_ShopGoods.Get(_pendingBoxGoodsId);
			_pendingBoxGoodsId = 0;

			if (success == false || data == null || row == null)
			{
				Debug.LogWarning($"[Shop] 보물상자 개봉 실패: {error}");
				return;
			}

			int keyItemId;
			if (tryGetKeyItemId(row, out keyItemId) == true)
			{
				Account.Instance.Inventory.TrySpend(keyItemId, row.Price);
			}

			List<GrantedReward> granted = new List<GrantedReward>();
			RewardGranter.FromServer(data.rewards, data.equipments, granted);
			RewardGranter.ApplyAll(granted);
			ShopPurchaseCounter.Increase(row.ID, row.UseDailyReset);

			if (_isDisposed == true)
			{
				return;
			}

			view.RefreshGoods(row.ID);
			UIManager.Instance.ShowRewardPopupAsync(granted, view.GetDestroyToken()).Forget();
		}

		private static bool tryGetKeyItemId(Table_ShopGoods.Row row, out int keyItemId)
		{
			keyItemId = 0;
			return row.PriceType == PriceType.Item && row.Price > 0
				&& int.TryParse(row.PriceParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out keyItemId) == true;
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
