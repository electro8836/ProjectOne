using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using ProjectOne.Audio;
using ProjectOne.Utils;

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
		[SerializeField] private List<TabObjectState> _objectStates;

		[Header("배지")]
		[SerializeField] private GameObject _lockObject;	// 자물쇠
		[SerializeField] private GameObject _alertObject;	// 획득/알림 점
		[SerializeField] private BadgeType _badge = BadgeType.None;

		[Header("Theme & Feedback")]
		[SerializeField] private ButtonThemeData _themeData;

		private Vector3 _originalScale = Vector3.one;
		private bool _pressActive;	// 눌림 스케일이 적용됐는지 — Up에서 반드시 원복 보장

		public TabState CurrentState => _currentState;

		// 클릭 시 자기 자신을 인자로 통지한다 (그룹이 어떤 탭이 눌렸는지 식별하기 위함).
		public event Action<UITabButton> OnTabClicked;

		private void Start()
		{
			_originalScale = transform.localScale;

			OnClickEvent += onSelfClicked;
			OnPointerDownEvent += playDownFeedback;
			OnPointerUpEvent += playUpFeedback;

			applyStateColors(true);
			applyStateObjects();
			applyBadge();
		}

		private void OnDestroy()
		{
			// 진행 중인 스케일/색상 트윈이 파괴된 대상에 접근하지 않도록 정리
			transform.DOKill();
			if (_graphicColorStates != null)
			{
				for (int i = 0; i < _graphicColorStates.Count; i++)
				{
					GraphicColorState cfg = _graphicColorStates[i];
					if (cfg == null || cfg.targetGraphic == null) { continue; }

					cfg.targetGraphic.DOKill();
				}
			}

			OnClickEvent -= onSelfClicked;
			OnPointerDownEvent -= playDownFeedback;
			OnPointerUpEvent -= playUpFeedback;
		}

		private void onSelfClicked()
		{
			// 이미 선택된 탭을 다시 누르면 전환이 없으므로 사운드/VFX 생략
			if (_currentState != TabState.Selected) { playClickFeedback(); }

			if (OnTabClicked != null) { OnTabClicked.Invoke(this); }
		}

		private void playClickFeedback()
		{
			if (_themeData == null) { return; }

			if (!string.IsNullOrEmpty(_themeData.sfxAddress) && AudioManager.HasInstance)
			{
				AudioManager.Instance.PlaySFX(_themeData.sfxAddress);
			}

			if (!string.IsNullOrEmpty(_themeData.vfxAddress) && VFXManager.HasInstance)
			{
				VFXManager.Instance.PlayOneShot(_themeData.vfxAddress, transform);
			}
		}

		private void playDownFeedback()
		{
			if (_themeData == null) { return; }

			// 사운드와 동일 조건 — 이미 선택된 탭(전환 없음)은 스케일도 생략
			if (_currentState == TabState.Selected) { return; }

			_pressActive = true;

			// 가로(x)를 줄이면 탭 사이로 배경이 비치므로 x,y를 개별 곱한다 (가로 유지는 pressedScale.x=1)
			Vector3 pressed = new Vector3(_originalScale.x * _themeData.pressedScale.x, _originalScale.y * _themeData.pressedScale.y, _originalScale.z);
			transform.DOKill();
			transform.DOScale(pressed, _themeData.animationDuration).SetEase(Ease.OutQuad);
		}

		private void playUpFeedback()
		{
			if (_themeData == null) { return; }

			// 눌림이 적용됐을 때만 원복 (전환 사이 상태 변화와 무관하게 대칭 보장)
			if (!_pressActive) { return; }

			_pressActive = false;
			transform.DOKill();
			transform.DOScale(_originalScale, _themeData.animationDuration).SetEase(Ease.OutBack);
		}

		// 상태 전환 — 색 적용 + 클릭 가능 여부 갱신
		public void SetState(TabState newState, bool immediate = false)
		{
			if (_currentState == newState) { return; }

			_currentState = newState;
			applyStateColors(immediate);
			applyStateObjects();
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

		private void applyStateObjects()
		{
			if (_objectStates == null) { return; }

			for (int i = 0; i < _objectStates.Count; i++)
			{
				TabObjectState cfg = _objectStates[i];
				if (cfg == null || cfg.targetObject == null) { continue; }

				cfg.targetObject.SetActive(cfg.GetActiveByState(_currentState));
			}
		}

		private void applyBadge()
		{
			if (_lockObject != null) { _lockObject.SetActive(_badge == BadgeType.Lock); }

			if (_alertObject != null) { _alertObject.SetActive(_badge == BadgeType.Alert); }
		}
	}
}
