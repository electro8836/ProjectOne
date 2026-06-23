using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업의 스탯/스킬 옵션 1줄. Text_Title(제목) + Text(값)만 채운다.
	// 스탯 슬롯은 제목+값, 스킬 슬롯은 값(스킬명)만 쓰며 제목은 숨긴다.
	public class EquipmentOptionSlot : MonoBehaviour
	{
		[SerializeField] private TMP_Text _titleText;	// Text_Title
		[SerializeField] private TMP_Text _valueText;	// Text

		// title 이 비어 있으면 제목 오브젝트를 숨긴다 (스킬 슬롯용).
		public void Set(string title, string value)
		{
			bool hasTitle = !string.IsNullOrEmpty(title);
			_titleText.gameObject.SetActive(hasTitle);
			if (hasTitle)
			{
				_titleText.text = title;
			}

			_valueText.text = value;
		}
	}
}
