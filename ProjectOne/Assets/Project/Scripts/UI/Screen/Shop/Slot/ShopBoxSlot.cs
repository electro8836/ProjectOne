using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using EDT;
using ProjectOne.Shop;
using ProjectOne.UserData;
using TMPro;
using UnityEngine;

namespace ProjectOne.UI
{
	// 보물상자 슬롯(GoodsType.Box). 열쇠 아이템으로 여는 뽑기성 상품이다.
	// 구매 버튼은 OpenButton 을 베이스의 _buyButton 에 물린다.
	//
	// 가격 자리에는 값 대신 **내가 가진 열쇠 수**를 보여준다.
	// 하나도 없으면 필요한 수를 빨간색으로 띄우고 버튼을 잠근다.
	public class ShopBoxSlot : ShopProductSlotBase
	{
		[Header("상자")]
		[SerializeField] private TMP_Text _descText;		// DsecText (프리펩 이름의 오타를 그대로 둔다)
		[SerializeField] private UIButton _infoButton;		// InfoButton — 확률 정보
		[SerializeField] private RectTransform _effect;		// EffectMask/Effect — 계속 도는 빛
		[SerializeField] private float _effectRotateDuration = 10f;	// 이펙트가 한 바퀴 도는 데 걸리는 시간(초)
		[SerializeField] private UIButton _decomposeCheckButton;	// Decomposition/CheckButton — 자동분해 체크
		[SerializeField] private GameObject _decomposeCheckMark;	// Decomposition/CheckButton/Image — 체크 표시

		private Color _amountColor = Color.white;	// 프리펩에 설정된 원래 수량 색
		private Tween _effectTween;

		protected override void Awake()
		{
			base.Awake();

			if (_priceText != null)
			{
				_amountColor = _priceText.color;
			}

			if (_infoButton != null)
			{
				_infoButton.OnClickEvent += onInfoClicked;
			}

			if (_decomposeCheckButton != null)
			{
				_decomposeCheckButton.OnClickEvent += onDecomposeCheckClicked;
			}
		}

		private void OnEnable()
		{
			startEffect();
		}

		private void OnDisable()
		{
			stopEffect();
		}

		protected override void OnDestroy()
		{
			stopEffect();

			if (_infoButton != null)
			{
				_infoButton.OnClickEvent -= onInfoClicked;
			}

			if (_decomposeCheckButton != null)
			{
				_decomposeCheckButton.OnClickEvent -= onDecomposeCheckClicked;
			}

			base.OnDestroy();
		}

		protected override UniTask onBindAsync(CancellationToken ct)
		{
			if (_descText != null)
			{
				_descText.text = row.Desc;
			}

			return UniTask.CompletedTask;
		}

		// 가격 자리를 이번에 열 상자 수로 바꾼다. 베이스가 버튼 상태를 정한 뒤에 불리므로 여기서 잠근 것이 최종이다.
		protected override void onPurchaseLimitApplied(int remaining)
		{
			bool decomposeAll = ShopBoxOpen.IsDecomposeChecked(row.ID);
			if (_decomposeCheckMark != null)
			{
				_decomposeCheckMark.SetActive(decomposeAll);
			}

			if (_priceText == null || row.PriceType != PriceType.Item)
			{
				return;
			}

			int itemId;
			if (int.TryParse(row.PriceParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) == false)
			{
				return;
			}

			// 자동분해면 보유한 만큼 전부, 아니면 한 번에 여는 상한과 인벤토리 빈칸으로 자른 수다.
			// 빈칸이 없으면 칸을 무시한 수를 그대로 보여준다 — 그 상태의 클릭은 가득 참 경고로 이어진다.
			bool noSpace = decomposeAll == false && ShopBoxOpen.GetFreeInventorySlots() <= 0;
			int openCount = ShopBoxOpen.GetOpenCount(row, decomposeAll, noSpace);
			bool enough = getOwnedCount(itemId) >= row.Price;

			// 하나도 없으면 "몇 개가 필요한지"를 빨간색으로 알린다.
			_priceText.text = (enough ? openCount : row.Price).ToString("N0", CultureInfo.InvariantCulture);
			_priceText.color = enough ? _amountColor : _lackColor;

			if (enough == false && _buyButton != null)
			{
				_buyButton.interactable = false;
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private static int getOwnedCount(int itemId)
		{
			Inventory inventory = Account.Instance.Inventory;
			if (inventory == null)
			{
				return 0;
			}

			return inventory.GetCount(itemId);
		}

		// 슬롯은 재사용되며 켜졌다 꺼지므로 트윈도 그에 맞춰 만들고 지운다.
		private void startEffect()
		{
			if (_effect == null || _effectTween != null || _effectRotateDuration <= 0f)
			{
				return;
			}

			_effectTween = _effect
				.DORotate(new Vector3(0f, 0f, -360f), _effectRotateDuration, RotateMode.FastBeyond360)
				.SetLoops(-1, LoopType.Restart)
				.SetEase(Ease.Linear);
		}

		private void stopEffect()
		{
			if (_effectTween != null)
			{
				_effectTween.Kill();
				_effectTween = null;
			}

			if (_effect != null)
			{
				_effect.DOKill();
			}
		}

		// 자동분해 체크를 뒤집고 열 상자 수를 다시 계산한다.
		private void onDecomposeCheckClicked()
		{
			if (row == null)
			{
				return;
			}

			ShopBoxOpen.SetDecomposeChecked(row.ID, ShopBoxOpen.IsDecomposeChecked(row.ID) == false);
			RefreshPurchaseLimit();
		}

		// 이 상자에서 무엇이 얼마나 나오는지 확률표를 연다.
		private void onInfoClicked()
		{
			if (row == null || row.RewardGroupID <= 0)
			{
				Debug.LogWarning($"[Shop] 보상 그룹이 없어 확률표를 열 수 없다 — goodsId={(row != null ? row.ID : 0)}");
				return;
			}

			UIManager.Instance.ShowBoxRewardPopupAsync(row.RewardGroupID, this.GetCancellationTokenOnDestroy()).Forget();
		}
	}
}
