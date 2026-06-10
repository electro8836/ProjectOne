using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 탭 상태별로 한 Graphic(Image/Text 등)의 색을 매핑한다.
	[Serializable]
	public class GraphicColorState
	{
		[Tooltip("색상을 변경할 UI 그래픽 대상 (Image, TextMeshProUGUI 등)")]
		public Graphic targetGraphic;

		[Header("State Colors")]
		public Color unselectedColor = Color.white;
		public Color selectedColor = new Color(0.2f, 0.8f, 1f, 1f);
		public Color lockedColor = Color.gray;

		public Color GetColorByState(TabState state)
		{
			switch (state)
			{
				case TabState.Selected:
					return selectedColor;

				case TabState.Locked:
					return lockedColor;

				default:
					return unselectedColor;
			}
		}
	}
}
