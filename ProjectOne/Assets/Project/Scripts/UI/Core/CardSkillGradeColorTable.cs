using System;
using System.Collections.Generic;
using UnityEngine;
using EDT;

namespace ProjectOne.UI
{
	// 카드스킬 등급별 색상 묶음 (BgMask/InnerDeco). 인스펙터로 주입해 카드 아이템이 등급으로 조회한다.
	// 캐릭터 등급 색상(CharacterGradeColorTable)과는 별도 데이터로 분리한다.
	[CreateAssetMenu(menuName = "Custom UI/Card Skill Grade Color Table", fileName = "CardSkillGradeColorTable")]
	public class CardSkillGradeColorTable : ScriptableObject
	{
		// 한 등급에 대한 두 요소 색상.
		[Serializable]
		public class GradeColor
		{
			public CardSkillGrade grade = CardSkillGrade.None;
			public Color bgMask = Color.white;
			public Color innerDeco = Color.white;
		}

		[SerializeField] private List<GradeColor> _colors = new List<GradeColor>();

		// 등급에 해당하는 색상 묶음. 없으면 첫 항목(또는 기본값) 반환.
		public GradeColor Get(CardSkillGrade grade)
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
