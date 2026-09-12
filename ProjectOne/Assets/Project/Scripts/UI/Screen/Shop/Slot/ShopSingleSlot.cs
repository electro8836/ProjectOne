using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 단일 상품 슬롯 — 재화(GoodsType.Currency)와 아이템(GoodsType.Item)이 쓴다.
	// 3열 그리드 안에 여러 개가 나란히 들어가는 유일한 슬롯이다.
	//
	// 무엇을 얼마나 주는지는 RewardGroupID 가 정한다. 단일 상품이라 구성은 항상 한 건이다.
	//   재화  — Image 에 상품 이미지, AmountText 에 수량
	//   아이템 — ItemSlotRoot 에 아이템칸 하나(수량은 그 칸이 표시), AmountText 는 숨김
	public class ShopSingleSlot : ShopProductSlotBase
	{
		[Header("보너스")]
		[SerializeField] private GameObject _bonusObject;		// Bonus
		[SerializeField] private TMP_Text _bonusText;			// Bonus/BonusText

		[Header("구성")]
		[SerializeField] private Image _currencyImage;			// Image — 재화 상품 이미지
		[SerializeField] private TMP_Text _amountText;			// AmountText — 재화 수량
		[SerializeField] private RectTransform _itemSlotRoot;		// ItemSlotRoot — 아이템칸 자리
		[SerializeField] private ItemSlot _itemSlotPrefab;		// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		// 상품 이미지는 파생이 직접 관리한다 — 베이스의 _goodsIcon 을 쓰면
		// 베이스의 비동기 로드가 켜는 것과 여기서 끄는 것이 겹친다.
		private readonly IconBinder _currencyIconBinder = new IconBinder();

		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>();

		private ItemSlot _itemSlot;
		private RewardPreviewItem _item;

		protected override void Awake()
		{
			base.Awake();

			_currencyIconBinder.Initialize(_currencyImage);
		}

		protected override void OnDestroy()
		{
			_currencyIconBinder.Release();

			if (_itemSlot != null)
			{
				_itemSlot.OnClicked -= onSlotClicked;
			}

			base.OnDestroy();
		}

		protected override async UniTask onBindAsync(CancellationToken ct)
		{
			applyBonus();

			_preview.Clear();
			RewardPreview.Build(row.RewardGroupID, _preview);

			// 단일 상품이라 구성은 한 건이다. 없으면 표시할 것도 없다.
			if (_preview.Count == 0)
			{
				_item = default(RewardPreviewItem);
				setCurrencyVisible(false);
				setItemSlotVisible(false);
				return;
			}

			_item = _preview[0];

			if (_item.equipment == null && _item.type == RewardType.Currency)
			{
				setItemSlotVisible(false);
				setCurrencyVisible(true);

				if (_amountText != null)
				{
					_amountText.text = _item.count.ToString("N0", CultureInfo.InvariantCulture);
				}

				await _currencyIconBinder.SetAsync(row.Icon, ct);
				return;
			}

			setCurrencyVisible(false);
			await bindItemSlotAsync(ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void applyBonus()
		{
			bool hasBonus = string.IsNullOrEmpty(row.Bonus) == false;

			if (_bonusObject != null)
			{
				_bonusObject.SetActive(hasBonus);
			}

			if (_bonusText != null && hasBonus == true)
			{
				_bonusText.text = row.Bonus;
			}
		}

		private void setCurrencyVisible(bool visible)
		{
			if (_currencyImage != null && visible == false)
			{
				_currencyImage.gameObject.SetActive(false);
			}

			if (_amountText != null)
			{
				_amountText.gameObject.SetActive(visible);
			}
		}

		private void setItemSlotVisible(bool visible)
		{
			if (_itemSlot != null)
			{
				_itemSlot.gameObject.SetActive(visible);
			}
		}

		private UniTask bindItemSlotAsync(CancellationToken ct)
		{
			if (_itemSlot == null)
			{
				if (_itemSlotPrefab == null || _itemSlotRoot == null)
				{
					return UniTask.CompletedTask;
				}

				_itemSlot = Instantiate(_itemSlotPrefab, _itemSlotRoot);
				_itemSlot.StretchToParent();
				_itemSlot.OnClicked += onSlotClicked;
			}

			_itemSlot.gameObject.SetActive(true);

			if (_item.equipment != null)
			{
				return _itemSlot.BindEquipmentAsync(_item.equipment, false, _gradeColors, ct);
			}

			Table_Item.Row itemRow = Table_Item.Get(_item.itemId);
			if (itemRow == null)
			{
				_itemSlot.gameObject.SetActive(false);
				return UniTask.CompletedTask;
			}

			return _itemSlot.BindItemAsync(itemRow, _item.count, _gradeColors, ct);
		}

		// 진열된 것은 내 것이 아니므로 읽기 전용 팝업만 연다.
		private void onSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			ShopRewardPopup.Show(_item, sender, this.GetCancellationTokenOnDestroy());
		}
	}
}
