using System;
using System.Collections.Generic;
using UnityEngine;
using EDT;

namespace ProjectOne.UI
{
	// 캐릭터 클래스타입별 픽토 아이콘 묶음. 인스펙터로 주입해 디테일 팝업 등에서 타입으로 조회한다.
	// (Class_Melee→PictoIcon_Attack, Class_Ranged→PictoIcon_Bow, Class_Mage→PictoIcon_Hat)
	[CreateAssetMenu(menuName = "Custom UI/Class Icon Table", fileName = "ClassIconTable")]
	public class ClassIconTable : ScriptableObject
	{
		// 한 클래스타입에 대한 아이콘.
		[Serializable]
		public class ClassIcon
		{
			public CharacterTypes type = CharacterTypes.None;
			public Sprite icon;
		}

		[SerializeField] private List<ClassIcon> _icons = new List<ClassIcon>();

		// 클래스타입에 해당하는 아이콘. 없으면 null.
		public Sprite Get(CharacterTypes type)
		{
			for (int i = 0; i < _icons.Count; i++)
			{
				if (_icons[i].type == type)
				{
					return _icons[i].icon;
				}
			}

			return null;
		}
	}
}
