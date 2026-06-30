using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.Dungeon;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 던전 카드스킬 구매창. 보유 카드 중 3장을 제시(CardSkillShopItem)하고, 장착슬롯 6칸(CardSkillEquipSlotView)을 보여준다.
	// 카드를 선택하면 구매, 장착슬롯을 선택하면 판매(반값)로 하단 BuyOrSell 버튼이 동작한다. Reroll 은 3장을 다시 뽑는다(무료).
	// 구매/판매/레벨업의 실제 판정과 Hero 적용은 DungeonCardShop 이 담당하고, 이 클래스는 표시/입력만 책임진다.
	public class CardSkillBuyUI : UIScreen
	{
		// 선택 종류
		private enum SelectionKind
		{
			None,
			ShopCard,
			EquipSlot,
		}

		[Header("재화")]
		[SerializeField] private TMP_Text _currencyText;	// Currency/Text_Currency

		[Header("후보 카드 (3장)")]
		[SerializeField] private CardSkillShopItem[] _cardItems;

		[Header("장착슬롯 (6칸)")]
		[SerializeField] private CardSkillEquipSlotView[] _equipSlots;

		[Header("버튼")]
		[SerializeField] private UIButton _closeButton;			// Button_01_Close
		[SerializeField] private UIButton _buyOrSellButton;		// Button_01_BuyOrSell
		[SerializeField] private TMP_Text _buyOrSellLabel;		// Button_01_BuyOrSell/Text (TMP)
		[SerializeField] private UIButton _rerollButton;		// Button_01_Reroll

		private SelectionKind _selKind = SelectionKind.None;
		private int _selIndex = -1;

		// roll 재사용 버퍼 — 잠금(유지) 카드 cardSkillId 제외목록 / 교체 대상 인덱스
		private readonly List<int> _excludeBuffer = new List<int>();
		private readonly List<int> _replaceBuffer = new List<int>();

		private Action<DungeonEssenceChangedEvent> _onEssenceChanged;
		private Action<CardSkillSlotChangedEvent> _onSlotChanged;

		private void Awake()
		{
			if (_cardItems != null)
			{
				for (int i = 0; i < _cardItems.Length; i++)
				{
					if (_cardItems[i] != null)
					{
						_cardItems[i].Init(i, onCardClicked);
					}
				}
			}

			if (_equipSlots != null)
			{
				for (int i = 0; i < _equipSlots.Length; i++)
				{
					if (_equipSlots[i] != null)
					{
						_equipSlots[i].Init(i, onSlotClicked);
					}
				}
			}

			if (_closeButton != null)
			{
				_closeButton.OnClickEvent += onCloseClicked;
			}

			if (_buyOrSellButton != null)
			{
				_buyOrSellButton.OnClickEvent += onBuyOrSellClicked;
			}

			if (_rerollButton != null)
			{
				_rerollButton.OnClickEvent += onRerollClicked;
			}

			_onEssenceChanged = onEssenceChanged;
			_onSlotChanged = onSlotChanged;
			EventManager.Instance.Subscribe<DungeonEssenceChangedEvent>(_onEssenceChanged);
			EventManager.Instance.Subscribe<CardSkillSlotChangedEvent>(_onSlotChanged);
		}

		private void OnDestroy()
		{
			if (_closeButton != null)
			{
				_closeButton.OnClickEvent -= onCloseClicked;
			}

			if (_buyOrSellButton != null)
			{
				_buyOrSellButton.OnClickEvent -= onBuyOrSellClicked;
			}

			if (_rerollButton != null)
			{
				_rerollButton.OnClickEvent -= onRerollClicked;
			}

			EventManager.Instance.Unsubscribe<DungeonEssenceChangedEvent>(_onEssenceChanged);
			EventManager.Instance.Unsubscribe<CardSkillSlotChangedEvent>(_onSlotChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			restoreShop();
			roll();
			refreshSlots();
			refreshCurrency();
			clearSelection();
			return UniTask.CompletedTask;
		}

		// 닫을 때 잠금(고정) 카드와 위치를 런 상태에 저장 — 다음에 다시 열 때 복원한다.
		public override UniTask OnCloseAsync()
		{
			saveShopState();
			return UniTask.CompletedTask;
		}

		// ── 입력 ──────────────────────────────────────────────────────

		private void onCardClicked(int index)
		{
			_selKind = SelectionKind.ShopCard;
			_selIndex = index;
			refreshSelectionVisual();
			refreshBuyOrSell();
		}

		private void onSlotClicked(int index)
		{
			if (_equipSlots == null || index < 0 || index >= _equipSlots.Length || _equipSlots[index].IsEmpty == true)
			{
				return;
			}

			_selKind = SelectionKind.EquipSlot;
			_selIndex = index;
			refreshSelectionVisual();
			refreshBuyOrSell();
		}

		private void onBuyOrSellClicked()
		{
			if (_selKind == SelectionKind.ShopCard)
			{
				CardSkillShopItem item = _cardItems[_selIndex];
				int cardSkillId = item.CardSkillId;
				// 구매 성공한 카드는 그 자리에서 소진(숨김)
				if (cardSkillId > 0 && DungeonCardShop.Instance.TryBuy(cardSkillId) == true)
				{
					item.MarkSold();
				}
			}
			else if (_selKind == SelectionKind.EquipSlot)
			{
				DungeonCardShop.Instance.TrySell(_selIndex);
			}

			// 슬롯/재화는 이벤트로 갱신됨 — 남은 카드의 레벨/가격/가용만 재평가하고 선택 해제.
			refreshCardItems();
			clearSelection();
		}

		private void onRerollClicked()
		{
			roll();
			clearSelection();
		}

		private void onCloseClicked()
		{
			UIManager.Instance.CloseOverlayAsync().Forget();
		}

		// ── 표시 갱신 ─────────────────────────────────────────────────

		// 런 상태에 저장된 잠금(고정) 카드를 자리별로 복원한다(미잠금/빈 자리는 이후 roll 이 채움).
		private void restoreShop()
		{
			for (int i = 0; i < _cardItems.Length; i++)
			{
				if (_cardItems[i] == null)
				{
					continue;
				}

				DungeonRunState.ShopCardState st = DungeonRunState.Instance.GetShopCard(i);
				if (st.locked == true && st.cardSkillId > 0)
				{
					int level;
					int price;
					bool buyable;
					if (DungeonCardShop.Instance.GetBuyInfo(st.cardSkillId, out level, out price, out buyable) == true)
					{
						bindCardItem(_cardItems[i], st.cardSkillId, level, price);
						_cardItems[i].SetLocked(true);
						_cardItems[i].SetInteractable(buyable);
						continue;
					}
				}

				_cardItems[i].Bind(0, string.Empty, string.Empty, 0, 0, null);
			}
		}

		// 현재 표시 중인 잠금 카드와 위치를 런 상태에 저장 (미잠금은 비워 다음에 새로 뽑히게)
		private void saveShopState()
		{
			for (int i = 0; i < _cardItems.Length; i++)
			{
				if (_cardItems[i] == null)
				{
					continue;
				}

				bool locked = _cardItems[i].IsLocked == true && _cardItems[i].HasCard == true;
				int id = locked == true ? _cardItems[i].CardSkillId : 0;
				DungeonRunState.Instance.SetShopCard(i, id, locked);
			}
		}

		// 잠금(유지) 카드는 그대로 두고, 나머지(미잠금·소진) 자리만 새 후보로 채운다.
		private void roll()
		{
			_excludeBuffer.Clear();
			_replaceBuffer.Clear();

			for (int i = 0; i < _cardItems.Length; i++)
			{
				if (_cardItems[i] == null)
				{
					continue;
				}

				if (_cardItems[i].IsLocked == true && _cardItems[i].HasCard == true)
				{
					// 잠금 카드는 유지 — 중복 방지 제외목록에 넣고 가격만 갱신
					_excludeBuffer.Add(_cardItems[i].CardSkillId);
					refreshOneCard(_cardItems[i]);
				}
				else
				{
					_replaceBuffer.Add(i);
				}
			}

			List<DungeonCardShop.ShopChoice> choices = DungeonCardShop.Instance.RollChoices(_replaceBuffer.Count, _excludeBuffer);
			for (int j = 0; j < _replaceBuffer.Count; j++)
			{
				CardSkillShopItem item = _cardItems[_replaceBuffer[j]];
				if (j < choices.Count)
				{
					bindCardItem(item, choices[j].cardSkillId, choices[j].displayLevel, choices[j].price);
				}
				else
				{
					item.Bind(0, string.Empty, string.Empty, 0, 0, null);
				}
			}
		}

		// 남은(표시 중인) 카드들의 레벨/가격/가용을 재평가 (구매·판매 후 — 잠금 보존)
		private void refreshCardItems()
		{
			for (int i = 0; i < _cardItems.Length; i++)
			{
				if (_cardItems[i] == null || _cardItems[i].HasCard == false)
				{
					continue;
				}

				refreshOneCard(_cardItems[i]);
			}
		}

		// 카드 1장의 레벨/가격/가용 갱신 (Bind 와 달리 잠금 상태는 유지)
		private void refreshOneCard(CardSkillShopItem item)
		{
			int level;
			int price;
			bool buyable;
			if (DungeonCardShop.Instance.GetBuyInfo(item.CardSkillId, out level, out price, out buyable) == true)
			{
				item.RefreshLevelPrice(level, price);
				item.SetInteractable(buyable);
			}
		}

		private void bindCardItem(CardSkillShopItem item, int cardSkillId, int level, int price)
		{
			Table_CardSkill.Row card = Table_CardSkill.Get(cardSkillId);
			string title = card != null ? card.Name : string.Empty;
			string desc = card != null ? card.Desc : string.Empty;
			item.Bind(cardSkillId, title, desc, level, price, iconFor(card));
			item.SetInteractable(true);
		}

		// 장착슬롯 6칸 갱신 (DungeonRunState 기준)
		private void refreshSlots()
		{
			for (int i = 0; i < _equipSlots.Length; i++)
			{
				if (_equipSlots[i] == null)
				{
					continue;
				}

				DungeonRunState.CardSkillSlot slot = DungeonRunState.Instance.GetSlot(i);
				Table_CardSkill.Row card = slot.cardSkillId > 0 ? Table_CardSkill.Get(slot.cardSkillId) : null;
				_equipSlots[i].Bind(slot.cardSkillId, iconFor(card));
			}
		}

		private void refreshCurrency()
		{
			if (_currencyText != null)
			{
				_currencyText.text = DungeonRunState.Instance.EssenceAmount.ToString();
			}
		}

		// 선택 강조(Focus/Select) 토글
		private void refreshSelectionVisual()
		{
			for (int i = 0; i < _cardItems.Length; i++)
			{
				if (_cardItems[i] != null)
				{
					_cardItems[i].SetFocus(_selKind == SelectionKind.ShopCard && i == _selIndex);
				}
			}

			for (int i = 0; i < _equipSlots.Length; i++)
			{
				if (_equipSlots[i] != null)
				{
					_equipSlots[i].SetSelected(_selKind == SelectionKind.EquipSlot && i == _selIndex);
				}
			}
		}

		// BuyOrSell 버튼 라벨/활성 갱신
		private void refreshBuyOrSell()
		{
			bool active = _selKind != SelectionKind.None;
			if (_buyOrSellButton != null)
			{
				_buyOrSellButton.interactable = active;
			}

			if (_buyOrSellLabel != null)
			{
				_buyOrSellLabel.text = _selKind == SelectionKind.EquipSlot ? "판매" : "구매";
			}
		}

		private void clearSelection()
		{
			_selKind = SelectionKind.None;
			_selIndex = -1;
			refreshSelectionVisual();
			refreshBuyOrSell();
		}

		private void onEssenceChanged(DungeonEssenceChangedEvent evt)
		{
			if (_currencyText != null)
			{
				_currencyText.text = evt.Amount.ToString();
			}
		}

		private void onSlotChanged(CardSkillSlotChangedEvent evt)
		{
			refreshSlots();
		}

		// 카드스킬 대표 아이콘 (아틀라스 동기 조회 — 없으면 null)
		private static Sprite iconFor(Table_CardSkill.Row card)
		{
			if (card == null || string.IsNullOrEmpty(card.Icon) == true)
			{
				return null;
			}

			return AtlasManager.Instance.Get(card.Icon);
		}
	}
}
