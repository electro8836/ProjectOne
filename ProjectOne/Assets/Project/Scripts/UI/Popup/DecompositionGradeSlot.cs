using System;
using UnityEngine;
using TMPro;
using EDT;

namespace ProjectOne.UI
{
	// 분해 팝업의 등급 선택 1칸(UIPrefb_DecompositionGradeSlot). 등급명과 체크 표시만 그린다 —
	// 선택 상태는 Presenter 가 들고 있다가 SetChecked 로 알려준다.
	public class DecompositionGradeSlot : MonoBehaviour
	{
		[SerializeField] private UIButton _checkButton;	// CheckButton
		[SerializeField] private GameObject _checkImage;	// CheckButton/Image — 선택 표시
		[SerializeField] private TMP_Text _gradeName;		// GradeName

		private ItemGradeType _grade;

		public event Action<ItemGradeType> OnClicked;

		public ItemGradeType Grade
		{
			get { return _grade; }
		}

		private void Awake()
		{
			_checkButton.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_checkButton.OnClickEvent -= onClicked;
		}

		// 등급명은 그 등급의 글자색으로 적는다(장비 팝업의 등급 표시와 같은 색).
		public void Bind(ItemGradeType grade, ItemGradeColorTable colors)
		{
			_grade = grade;
			_gradeName.text = ItemGradeNames.Get(grade);

			if (colors != null)
			{
				_gradeName.color = colors.Get(grade).text;
			}
		}

		public void SetChecked(bool value)
		{
			_checkImage.SetActive(value);
		}

		private void onClicked()
		{
			if (OnClicked != null) { OnClicked.Invoke(_grade); }
		}
	}
}
