using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 던전 구매창의 카드 1장(후보). 본체 클릭은 선택(구매 대상), Button_Lock 은 잠금 토글(리롤 시 유지).
	// 표시/입력 전용 — 구매/판매 판정과 적용은 CardSkillBuyUI/DungeonCardShop 이 담당한다.
	public class CardSkillShopItem : MonoBehaviour
	{
		// 잠금 비주얼 색상
		private static readonly Color FrameNormal = new Color(0x60 / 255f, 0x60 / 255f, 0x60 / 255f, 1f);
		private static readonly Color FrameLocked = Color.white;
		private static readonly Color TextNormal = Color.white;
		private static readonly Color TextLocked = Color.black;

		[Header("버튼")]
		[SerializeField] private UIButton _selectButton;	// 카드 본체 (선택/구매 대상)
		[SerializeField] private UIButton _lockButton;		// Button_Lock (잠금 토글)

		[Header("내용")]
		[SerializeField] private Image _icon;			// Card/CardSkillIcon
		[SerializeField] private Image _focus;			// Focus (선택 표시)
		[SerializeField] private TMP_Text _titleText;	// Text_Title
		[SerializeField] private TMP_Text _levelText;	// Text_Level
		[SerializeField] private TMP_Text _descText;	// Text_Description
		[SerializeField] private TMP_Text _priceText;	// Price/Text_Price

		[Header("잠금 비주얼")]
		[SerializeField] private Image _lockFrame;		// Button_Lock/Frame (잠금 시 흰색)
		[SerializeField] private TMP_Text _lockText;	// Button_Lock/Text_Fix (잠금 시 검정)

		private Action<int> _onSelectClicked;
		private int _index;
		private int _cardSkillId;
		private bool _isLocked;

		public int CardSkillId => _cardSkillId;
		public bool IsLocked => _isLocked;

		// 화면에 표시 중인 유효 카드인지 (소진되면 SetActive(false) 라 false)
		public bool HasCard => _cardSkillId > 0 && gameObject.activeSelf == true;

		private void Awake()
		{
			if (_selectButton != null)
			{
				_selectButton.OnClickEvent += onSelectClicked;
			}

			if (_lockButton != null)
			{
				_lockButton.OnClickEvent += onLockClicked;
			}
		}

		private void OnDestroy()
		{
			if (_selectButton != null)
			{
				_selectButton.OnClickEvent -= onSelectClicked;
			}

			if (_lockButton != null)
			{
				_lockButton.OnClickEvent -= onLockClicked;
			}
		}

		// 부모가 인덱스와 선택 콜백을 1회 주입
		public void Init(int index, Action<int> onSelectClicked)
		{
			_index = index;
			_onSelectClicked = onSelectClicked;
		}

		// 새 카드 표시 바인딩 (cardSkillId<=0 이면 빈 카드로 숨김). 잠금은 해제 상태로 리셋.
		public void Bind(int cardSkillId, string title, string desc, int level, int price, Sprite icon)
		{
			_cardSkillId = cardSkillId;
			_isLocked = false;
			gameObject.SetActive(cardSkillId > 0);
			if (cardSkillId <= 0)
			{
				return;
			}

			if (_titleText != null)
			{
				_titleText.text = title;
			}

			if (_descText != null)
			{
				_descText.text = desc;
			}

			if (_levelText != null)
			{
				_levelText.text = "Lv." + level.ToString();
			}

			if (_priceText != null)
			{
				_priceText.text = price.ToString();
			}

			if (_icon != null)
			{
				_icon.sprite = icon;
			}

			SetFocus(false);
			applyLockVisual();
		}

		// 가격/레벨만 갱신 (구매·판매로 슬롯이 바뀐 뒤 유지 카드의 표시 갱신용)
		public void RefreshLevelPrice(int level, int price)
		{
			if (_levelText != null)
			{
				_levelText.text = "Lv." + level.ToString();
			}

			if (_priceText != null)
			{
				_priceText.text = price.ToString();
			}
		}

		// 구매 소진 — 카드를 숨기고 잠금 해제
		public void MarkSold()
		{
			_cardSkillId = 0;
			_isLocked = false;
			gameObject.SetActive(false);
		}

		// 잠금 상태 직접 설정 (런 상태 복원용)
		public void SetLocked(bool locked)
		{
			_isLocked = locked;
			applyLockVisual();
		}

		public void SetFocus(bool on)
		{
			if (_focus != null)
			{
				_focus.gameObject.SetActive(on);
			}
		}

		public void SetInteractable(bool on)
		{
			if (_selectButton != null)
			{
				_selectButton.interactable = on;
			}
		}

		private void onSelectClicked()
		{
			if (_onSelectClicked != null)
			{
				_onSelectClicked(_index);
			}
		}

		// 잠금 토글 — 비주얼만 갱신(리롤 유지 여부는 부모가 IsLocked 로 조회)
		private void onLockClicked()
		{
			if (_cardSkillId <= 0)
			{
				return;
			}

			_isLocked = !_isLocked;
			applyLockVisual();
		}

		// 잠금 상태에 따른 Frame/Text 색상 적용
		private void applyLockVisual()
		{
			if (_lockFrame != null)
			{
				_lockFrame.color = _isLocked ? FrameLocked : FrameNormal;
			}

			if (_lockText != null)
			{
				_lockText.color = _isLocked ? TextLocked : TextNormal;
			}
		}
	}
}
