using System;
using UnityEngine;

namespace ProjectOne.UI
{
	// "누르면 이 맵으로 이동한다"를 버튼 자신이 들고 있게 하는 얇은 어댑터.
	// ScreenOpenButton 과 같은 구조다 — 버튼은 자기 대상만 알고, **가는 결정은 하지 않는다.**
	//
	// 목적지를 Table_Map.ID 하나로 표현하는 이유 —
	// Map 테이블이 이미 MapType(Town/Field/Dungeon)을 들고 있어서 코드가 목적지 분류표를
	// 따로 가질 필요가 없다. 여기에 enum 을 새로 만들면 테이블과 두 벌이 되어 같이 틀어진다.
	//
	// 지금은 개발용 이동 버튼이 쓴다. 포탈·필드선택 UI 도 같은 대상 표현을 쓰면 된다.
	[RequireComponent(typeof(UIButton))]
	public class DevWarpButton : MonoBehaviour
	{
		[Tooltip("이동할 맵의 Table_Map.ID (마을 10001 / 필드 1-1 20101)")]
		[SerializeField] private int _mapId;

		private UIButton _button;

		public event Action<int> OnClicked;

		public int MapId
		{
			get { return _mapId; }
		}

		private void Awake()
		{
			_button = this.GetComponent<UIButton>();
			_button.OnClickEvent += onButtonClicked;
		}

		private void OnDestroy()
		{
			if (_button != null)
			{
				_button.OnClickEvent -= onButtonClicked;
			}
		}

		private void onButtonClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(_mapId);
			}
		}
	}
}
