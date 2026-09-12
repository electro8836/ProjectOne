using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Shop;
using UnityEngine;

namespace ProjectOne.UI
{
	// 상점 화면 Presenter — 어떤 카테고리를 그릴지 정하고 구매 입력을 받는다.
	//
	// 구매는 아직 서버에 나가지 않는다. 지금은 로그를 남기고 구매 횟수만 올려
	// 제한이 걸린 상품의 표시가 실제로 줄어드는지 확인할 수 있게 해 둔다.
	public sealed class ShopPresenter : Presenter<ShopUI>
	{
		private IReadOnlyList<Table_ShopCategory.Row> _categories;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		protected override void OnInitialize()
		{
			view.OnTabChanged += onTabChanged;
			view.OnBuyClicked += onBuyClicked;
			view.OnHomeClicked += onHomeClicked;
		}

		protected override void OnDispose()
		{
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

			// TODO(서버) — 실제 구매 요청으로 교체한다. 결제 검증·재화 차감·보상 지급 모두 서버 권위여야 한다.
			Debug.Log($"[Shop] 구매 요청 goodsId={row.ID} name={row.Name} goodsType={row.GoodsType} priceType={row.PriceType} price={row.Price} priceParam={row.PriceParam} rewardGroupId={row.RewardGroupID}");

			ShopPurchaseCounter.Increase(row.ID, row.UseDailyReset);
			view.RefreshGoods(row.ID);
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
