using System;
using System.Collections.Generic;
using UnityEngine;
using EDT;

namespace ProjectOne.UI
{
	// 아이템 등급별 색상 묶음 (Bg/Border/Text). 인스펙터로 주입해 슬롯·팝업이 등급으로 조회한다.
	[CreateAssetMenu(menuName = "Custom UI/Item Grade Color Table", fileName = "ItemGradeColorTable")]
	public class ItemGradeColorTable : ScriptableObject
	{
		// 한 등급에 대한 세 요소 색상.
		[Serializable]
		public class GradeColor
		{
			public ItemGradeType grade = ItemGradeType.None;
			public Color bg = Color.white;		// Bg_Mask / TopBg
			public Color border = Color.white;	// Border / Deco2
			public Color text = Color.white;	// 등급명 텍스트
		}

		[SerializeField] private List<GradeColor> _colors = new List<GradeColor>();

		// 등급에 해당하는 색상 묶음. 없으면 첫 항목(또는 기본값) 반환.
		public GradeColor Get(ItemGradeType grade)
		{
			for (int i = 0; i < _colors.Count; i++)
			{
				if (_colors[i].grade == grade)
				{
					return _colors[i];
				}
			}

			if (_colors.Count > 0)
			{
				return _colors[0];
			}

			return new GradeColor();
		}
	}
}
