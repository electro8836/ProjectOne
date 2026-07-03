using System;
using System.Collections.Generic;
using UnityEngine;
using EDT;

namespace ProjectOne.UI
{
	// 재료 등급별 색상 묶음 (BgMask/Gradient/Glow). 인스펙터로 주입해 슬롯이 등급으로 조회한다.
	// 장비 등급 색상(GradeColorTable)과 동일 구조이며 키만 MaterialGradeType 이다.
	[CreateAssetMenu(menuName = "Custom UI/Material Grade Color Table", fileName = "MaterialGradeColorTable")]
	public class MaterialGradeColorTable : ScriptableObject
	{
		// 한 등급에 대한 세 요소 색상.
		[Serializable]
		public class GradeColor
		{
			public MaterialGradeType grade = MaterialGradeType.None;
			public Color bgMask = Color.white;
			public Color gradient = Color.white;
			public Color glow = Color.white;
		}

		[SerializeField] private List<GradeColor> _colors = new List<GradeColor>();

		// 등급에 해당하는 색상 묶음. 없으면 첫 항목(또는 기본값) 반환.
		public GradeColor Get(MaterialGradeType grade)
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
