using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 던전 구매창 하단의 카드스킬 장착슬롯 1칸. 장착된 카드를 표시하고, 클릭 시 부모에 인덱스를 알린다.
	// 표시 전용 — 판매 판정/적용은 CardSkillBuyUI/DungeonCardShop 이 담당한다.
	public class CardSkillEquipSlotView : MonoBehaviour
	{
		[SerializeField] private UIButton _button;	// 슬롯 클릭 영역
		[SerializeField] private Image _icon;		// Icon (장착 카드 아이콘)
		[SerializeField] private Image _select;		// Select (선택 표시)

		private Action<int> _onClicked;
		private int _index;
		private int _cardSkillId;

		public int CardSkillId => _cardSkillId;
		public bool IsEmpty => _cardSkillId <= 0;

		private void Awake()
		{
			if (_button != null)
			{
				_button.OnClickEvent += onClicked;
			}
		}

		private void OnDestroy()
		{
			if (_button != null)
			{
				_button.OnClickEvent -= onClicked;
			}
		}

		public void Init(int index, Action<int> onClicked)
		{
			_index = index;
			_onClicked = onClicked;
		}

		// 슬롯 표시 바인딩 (cardSkillId<=0 이면 빈 슬롯)
		public void Bind(int cardSkillId, Sprite icon)
		{
			_cardSkillId = cardSkillId;
			bool has = cardSkillId > 0;
			if (_icon != null)
			{
				_icon.gameObject.SetActive(has);
				_icon.sprite = has ? icon : null;
			}

			SetSelected(false);
		}

		public void SetSelected(bool on)
		{
			if (_select != null)
			{
				_select.gameObject.SetActive(on);
			}
		}

		private void onClicked()
		{
			if (_onClicked != null)
			{
				_onClicked(_index);
			}
		}
	}
}
