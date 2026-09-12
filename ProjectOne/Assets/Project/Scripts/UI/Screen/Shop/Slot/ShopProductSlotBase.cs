using System;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Resources;
using ProjectOne.Shop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 상품 슬롯의 공통 뼈대. GoodsType 별로 파생 클래스가 하나씩 있고, 프리펩도 1:1 로 대응한다.
	//
	// 여기서는 모든 상품이 공통으로 가지는 것만 다룬다 — 이름, 상품 아이콘, 가격, 구매 버튼, 구매 제한.
	// 그 상품 종류에만 있는 것(상자의 확률 정보, 패스의 기간, 패키지의 구성품 등)은 파생이 맡는다.
	public abstract class ShopProductSlotBase : MonoBehaviour
	{
		[Header("공통")]
		[SerializeField] protected TMP_Text _titleText;		// TitleText
		[SerializeField] protected Image _goodsIcon;		// 상품 대표 이미지 (없는 프리펩은 비워 둔다)
		[SerializeField] protected UIButton _buyButton;		// BuyButton / OpenButton
		[SerializeField] protected Image _priceIcon;		// BuyButton/Group/Icon
		[SerializeField] protected TMP_Text _priceText;		// BuyButton/Group/ValueText
		[SerializeField] protected TMP_Text _limitText;		// 남은 구매 횟수 표시칸 (없으면 표시를 생략한다)
		[SerializeField] protected Color _lackColor = Color.red;	// 더 살 수 없거나 재료가 모자랄 때 쓰는 색

		// 더 살 수 없는 상품의 가격 자리에 대신 넣는 문구.
		private const string SOLD_OUT_TEXT = "구매함";

		// 구매 버튼이 눌렸음을 상품 ID 와 함께 통지한다.
		public event Action<int> OnBuyClicked;

		// 지금 그리고 있는 상품의 ID. 화면이 특정 상품의 슬롯만 찾아 갱신할 때 쓴다.
		public int GoodsId
		{
			get { return row != null ? row.ID : 0; }
		}

		private readonly IconBinder _goodsIconBinder = new IconBinder();
		private readonly IconBinder _priceIconBinder = new IconBinder();

		protected Table_ShopGoods.Row row;

		private Color _limitColor = Color.white;	// 프리펩에 설정된 LimitText 원래 색

		protected virtual void Awake()
		{
			if (_limitText != null)
			{
				_limitColor = _limitText.color;
			}

			_goodsIconBinder.Initialize(_goodsIcon);
			_priceIconBinder.Initialize(_priceIcon);

			if (_buyButton != null)
			{
				_buyButton.OnClickEvent += onBuyClicked;
			}
		}

		protected virtual void OnDestroy()
		{
			_goodsIconBinder.Release();
			_priceIconBinder.Release();

			if (_buyButton != null)
			{
				_buyButton.OnClickEvent -= onBuyClicked;
			}
		}

		// 상품 한 건을 그린다. 공통 부분을 채운 뒤 파생에게 자기 몫을 넘긴다.
		public async UniTask BindAsync(Table_ShopGoods.Row data, CancellationToken ct)
		{
			row = data;
			if (row == null)
			{
				return;
			}

			if (_titleText != null)
			{
				_titleText.text = row.Name;
			}

			applyPurchaseLimit();

			UniTask goodsTask = _goodsIconBinder.SetAsync(row.Icon, ct);
			UniTask priceTask = _priceIconBinder.SetAsync(getPriceIconAddress(), ct);
			UniTask ownTask = onBindAsync(ct);

			await UniTask.WhenAll(goodsTask, priceTask, ownTask).SuppressCancellationThrow();
		}

		// 구매 제한 표시만 다시 계산한다 — 구매 직후 슬롯 전체를 다시 그리지 않기 위해서다.
		public void RefreshPurchaseLimit()
		{
			applyPurchaseLimit();
		}

		// 파생 훅 — 자기 프리펩에만 있는 필드를 채운다.
		protected virtual UniTask onBindAsync(CancellationToken ct)
		{
			return UniTask.CompletedTask;
		}

		// 파생 훅 — 구매 제한이 갱신된 직후, 가격 표시를 바꿔 낄 자리.
		// remaining 이 UNLIMITED 면 무제한, 0 이면 더 살 수 없는 상태다.
		protected virtual void onPurchaseLimitApplied(int remaining)
		{
		}

		// ── 가격 ──────────────────────────────────────────────────────────

		// PriceParam 은 PriceType 이 해석 방법을 결정한다 — 재화면 enum 이름, 아이템이면 아이템 ID 다.
		private string getPriceIconAddress()
		{
			switch (row.PriceType)
			{
				case PriceType.Currency:
				{
					EDT.Currency currency;
					if (Enum.TryParse<EDT.Currency>(row.PriceParam, false, out currency) == false)
					{
						return string.Empty;
					}

					Table_Currency.Row currencyRow = Table_Currency.Get(currency);
					return currencyRow != null ? currencyRow.Icon : string.Empty;
				}

				case PriceType.Item:
				{
					int itemId;
					if (int.TryParse(row.PriceParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) == false)
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

		private void applyPriceText()
		{
			if (_priceText == null)
			{
				return;
			}

			switch (row.PriceType)
			{
				case PriceType.Currency:
				case PriceType.Item:
					_priceText.text = row.Price.ToString("N0", CultureInfo.InvariantCulture);
					break;

				case PriceType.Cash:
					_priceText.text = $"₩{row.Price.ToString("N0", CultureInfo.InvariantCulture)}";
					break;

				case PriceType.Ad:
					_priceText.text = "광고 보기";
					break;

				case PriceType.Free:
					_priceText.text = "무료";
					break;

				default:
					_priceText.text = string.Empty;
					break;
			}
		}

		// ── 구매 제한 ─────────────────────────────────────────────────────

		private void applyPurchaseLimit()
		{
			int remaining = ShopPurchaseCounter.GetRemaining(row);
			bool unlimited = remaining == ShopPurchaseCounter.UNLIMITED;

			// 먼저 정상 가격으로 되돌린다 — 다시 그릴 때도 표시가 맞게 복구된다.
			applyPriceText();

			// 하루마다 되살아나지 않는 상품이 소진되면 가격 대신 구매 완료를 알린다.
			// 일일 리셋 상품은 내일 다시 살 수 있으므로 가격을 그대로 두고 남은 횟수로만 알린다.
			if (remaining == 0 && row.UseDailyReset == false && _priceText != null)
			{
				_priceText.text = SOLD_OUT_TEXT;
			}

			if (_limitText != null)
			{
				if (unlimited == true)
				{
					_limitText.gameObject.SetActive(false);
				}
				else
				{
					_limitText.gameObject.SetActive(true);
					_limitText.text = $"{remaining}/{row.MaxPurchaseCount}";

					// 더 살 수 없으면 남은 횟수도 빨갛게 알린다.
					_limitText.color = remaining > 0 ? _limitColor : _lackColor;
				}
			}

			if (_buyButton != null)
			{
				_buyButton.interactable = unlimited == true || remaining > 0;
			}

			// 파생이 마지막에 손본다 — 가격 표시든 버튼 잠금이든 여기서 한 것이 최종이다.
			onPurchaseLimitApplied(remaining);
		}

		private void onBuyClicked()
		{
			if (row == null)
			{
				return;
			}

			if (OnBuyClicked != null)
			{
				OnBuyClicked.Invoke(row.ID);
			}
		}

		// ── 아이콘 ────────────────────────────────────────────────────────

		// 아이콘 한 칸의 로드/해제 상태. 아틀라스에 있으면 동기로 끝내고, 아니면 참조카운트를 걸어 비동기로 받는다.
		// 상품 아이콘과 가격 아이콘이 같은 규칙을 쓰므로 한 벌로 묶었다.
		protected sealed class IconBinder
		{
			private Image _image;
			private string _address;	// 참조카운트 해제 대상. 아틀라스에서 꺼낸 경우엔 비어 있다

			public void Initialize(Image target)
			{
				_image = target;
			}

			public async UniTask SetAsync(string address, CancellationToken ct)
			{
				if (_image == null)
				{
					return;
				}

				Release();

				if (string.IsNullOrEmpty(address) == true)
				{
					_image.sprite = null;
					_image.gameObject.SetActive(false);
					return;
				}

				_address = address;

				Sprite atlasSprite = AtlasManager.Instance.Get(address);
				if (atlasSprite != null)
				{
					_image.sprite = atlasSprite;
					_image.gameObject.SetActive(true);
					_address = null;	// 참조카운트 대상이 아니다 — 해제가 헛돌지 않도록 지운다
					return;
				}

				// 칸을 통째로 끈다 — Image 만 끄면 LayoutElement 가 가로 레이아웃에서 자리를 계속 차지한다.
				_image.gameObject.SetActive(false);

				(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
				if (cancelled == true)
				{
					return;
				}

				// 늦게 도착한 로드가 그 사이 바뀐 아이콘을 덮어쓰지 않도록 확인한다.
				if (_address != address)
				{
					return;
				}

				if (icon != null)
				{
					_image.sprite = icon;
					_image.gameObject.SetActive(true);
				}
			}

			public void Release()
			{
				if (string.IsNullOrEmpty(_address) == false && ResourceManager.HasInstance)
				{
					ResourceManager.Instance.Release(_address);
				}

				_address = null;
			}
		}
	}
}
