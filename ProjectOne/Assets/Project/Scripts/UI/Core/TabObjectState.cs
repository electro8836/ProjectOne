using System;
using UnityEngine;

namespace ProjectOne.UI
{
	// 탭 상태별로 한 게임오브젝트의 활성 여부를 매핑한다.
	[Serializable]
	public class TabObjectState
	{
		[Tooltip("상태별로 켜고 끌 게임오브젝트 대상")]
		public GameObject targetObject;

		[Header("State Active")]
		public bool selectedActive = true;
		public bool unselectedActive = false;
		public bool lockedActive = false;

		public bool GetActiveByState(TabState state)
		{
			switch (state)
			{
				case TabState.Selected:
					return selectedActive;

				case TabState.Locked:
					return lockedActive;

				default:
					return unselectedActive;
			}
		}
	}
}
