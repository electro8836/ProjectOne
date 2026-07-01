using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using EDT;

namespace ProjectOne.UI
{
	// 던전 구매창 하단의 카드스킬 장착슬롯 1칸. 장착된 카드를 표시하고, 클릭 시 부모에 인덱스를 알린다.
	// 표시 전용 — 판매 판정/적용은 CardSkillBuyUI/DungeonCardShop 이 담당한다.
	public class CardSkillEquipSlotView : MonoBehaviour
	{
		[SerializeField] private UIButton _button;	// 슬롯 클릭 영역
		[SerializeField] private Image _icon;		// Icon (장착 카드 아이콘)
		[SerializeField] private Image _select;		// Select (선택 표시)

		[Header("등급 비주얼")]
		[SerializeField] private GameObject _emptySlot;	// EmptySlot (빈 슬롯 표시)
		[SerializeField] private Image _bgMask;			// BG_Mask (등급별 프레임 적용 대상)
		[SerializeField] private List<GradeFrame> _gradeFrames = new List<GradeFrame>();	// 등급별 프레임 스프라이트

		// 등급별 프레임 스프라이트 매핑 (카드 등급을 키로 스프라이트 조회)
		[Serializable]
		private class GradeFrame
		{
			public CardSkillGrade grade = CardSkillGrade.None;
			public Sprite frame;
		}

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

		// 슬롯 표시 바인딩 (cardSkillId<=0 이면 빈 슬롯 → EmptySlot 활성, Icon/BG_Mask 비활성)
		public void Bind(int cardSkillId, Sprite icon, CardSkillGrade grade)
		{
			_cardSkillId = cardSkillId;
			bool has = cardSkillId > 0;

			if (_emptySlot != null)
			{
				_emptySlot.SetActive(has == false);
			}

			if (_icon != null)
			{
				_icon.gameObject.SetActive(has);
				_icon.sprite = has ? icon : null;
			}

			if (_bgMask != null)
			{
				_bgMask.gameObject.SetActive(has);
				if (has == true)
				{
					_bgMask.sprite = frameFrom(_gradeFrames, grade);
				}
			}

			SetSelected(false);
		}

		// 등급에 해당하는 프레임 스프라이트 (없으면 null)
		private static Sprite frameFrom(List<GradeFrame> frames, CardSkillGrade grade)
		{
			for (int i = 0; i < frames.Count; i++)
			{
				if (frames[i].grade == grade)
				{
					return frames[i].frame;
				}
			}

			return null;
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
