using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace ProjectOne.UI
{
	// 탭 상태 — 색상과 클릭 가능 여부를 결정
	public enum TabState { Selected, Unselected, Locked }

	// 우상단 배지 슬롯 — 한 번에 하나만 표시
	public enum BadgeType { None, Lock, Alert }

	// 탭 버튼. 입력은 베이스가 처리하고, 여기서는 상태별 색 전환과 배지를 담당한다.
	// 클릭 시 어떤 윈도우를 열지는 외부에서 OnClickEvent를 구독해 결정한다.
	[AddComponentMenu("Custom UI/UI Tab Button")]
	public class UITabButton : UIBaseInteractable
	{
		[Header("Tab State")]
		[SerializeField] private TabState _currentState = TabState.Unselected;
		[SerializeField] private float _colorTransitionDuration = 0.2f;
		[SerializeField] private List<GraphicColorState> _graphicColorStates;

		[Header("배지")]
		[SerializeField] private GameObject _lockObject;	// 자물쇠
		[SerializeField] private GameObject _alertObject;	// 획득/알림 점
		[SerializeField] private BadgeType _badge = BadgeType.None;

		public TabState CurrentState => _currentState;

		private void Start()
		{
			applyStateColors(true);
			applyBadge();
		}

		// 상태 전환 — 색 적용 + 클릭 가능 여부 갱신
		public void SetState(TabState newState, bool immediate = false)
		{
			if (_currentState == newState) { return; }

			_currentState = newState;
			applyStateColors(immediate);
			interactable = _currentState != TabState.Locked;
		}

		// 배지 전환 — Lock/Alert 중 하나만 표시, 나머지는 숨김
		public void SetBadge(BadgeType badge)
		{
			_badge = badge;
			applyBadge();
		}

		private void applyStateColors(bool immediate)
		{
			if (_graphicColorStates == null) { return; }

			for (int i = 0; i < _graphicColorStates.Count; i++)
			{
				GraphicColorState cfg = _graphicColorStates[i];
				if (cfg == null || cfg.targetGraphic == null) { continue; }

				Color target = cfg.GetColorByState(_currentState);
				cfg.targetGraphic.DOKill();	// 진행 중 트윈 취소
				if (immediate)
				{
					cfg.targetGraphic.color = target;
				}
				else
				{
					cfg.targetGraphic.DOColor(target, _colorTransitionDuration);
				}
			}
		}

		private void applyBadge()
		{
			if (_lockObject != null) { _lockObject.SetActive(_badge == BadgeType.Lock); }

			if (_alertObject != null) { _alertObject.SetActive(_badge == BadgeType.Alert); }
		}
	}
}
