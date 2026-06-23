using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.UI
{
	// 자식 탭버튼들을 묶어 배타 선택만 담당하는 범용 컴포넌트.
	// "선택되면 무엇을 하는가"(화면 전환/리스트 필터 등)는 구독자가 OnTabChanged로 결정한다.
	// UI/오버레이/게임 enum을 전혀 모르므로 로비 메뉴 탭·인벤토리 분류 탭 등에 그대로 재사용된다.
	public class TabGroup : MonoBehaviour
	{
		private UITabButton[] _tabs;

		// 선택된 탭의 그룹 내 인덱스를 통지 (사용처가 enum 등으로 해석).
		public event Action<int> OnTabChanged;

		public int SelectedIndex { get; private set; } = -1;

		private void Awake()
		{
			_tabs = GetComponentsInChildren<UITabButton>(true);

			for (int i = 0; i < _tabs.Length; i++)
			{
				_tabs[i].OnTabClicked += onTabClicked;
			}
		}

		private void OnDestroy()
		{
			if (_tabs == null)
			{
				return;
			}

			for (int i = 0; i < _tabs.Length; i++)
			{
				_tabs[i].OnTabClicked -= onTabClicked;
			}
		}

		private void onTabClicked(UITabButton clicked)
		{
			int index = indexOf(clicked);
			if (index < 0)
			{
				return;
			}

			Select(index);
			if (OnTabChanged != null) { OnTabChanged.Invoke(index); }
		}

		// 지정 인덱스 탭만 Selected, 나머지는 Unselected (Locked는 건드리지 않음).
		public void Select(int index)
		{
			SelectedIndex = index;

			for (int i = 0; i < _tabs.Length; i++)
			{
				UITabButton tab = _tabs[i];
				if (tab.CurrentState == TabState.Locked)
				{
					continue;
				}

				tab.SetState(i == index ? TabState.Selected : TabState.Unselected);
			}
		}

		// 모든 탭을 Unselected (Locked는 건드리지 않음).
		public void ClearSelection()
		{
			SelectedIndex = -1;

			for (int i = 0; i < _tabs.Length; i++)
			{
				UITabButton tab = _tabs[i];
				if (tab.CurrentState == TabState.Locked)
				{
					continue;
				}

				tab.SetState(TabState.Unselected);
			}
		}

		private int indexOf(UITabButton tab)
		{
			for (int i = 0; i < _tabs.Length; i++)
			{
				if (_tabs[i] == tab)
				{
					return i;
				}
			}

			return -1;
		}
	}
}
