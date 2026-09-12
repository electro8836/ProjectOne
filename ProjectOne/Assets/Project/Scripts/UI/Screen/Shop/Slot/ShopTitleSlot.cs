using TMPro;
using UnityEngine;

namespace ProjectOne.UI
{
	// 상품 그룹의 제목 줄. ShopGoodsGroup 하나당 하나씩 Content 맨 위에 놓인다.
	public class ShopTitleSlot : MonoBehaviour
	{
		[SerializeField] private TMP_Text _titleText;	// TitleText

		public void SetTitle(string title)
		{
			if (_titleText != null)
			{
				_titleText.text = title;
			}
		}
	}
}
