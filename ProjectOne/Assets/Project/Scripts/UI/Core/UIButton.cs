using UnityEngine;
using DG.Tweening;
using ProjectOne.Audio;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 일반 버튼. 입력은 베이스가 처리하고, 여기서는 ButtonThemeData 기반 연출만 담당한다.
	// 클릭 시 윈도우 열기/닫기 등은 외부에서 OnClickEvent를 구독해 연결한다.
	[AddComponentMenu("Custom UI/UI Button")]
	public class UIButton : UIBaseInteractable
	{
		[Header("Theme & Feedback")]
		[SerializeField] private ButtonThemeData _themeData;
		[SerializeField] private RectTransform _targetGraphic;

		private Vector2 _originalPosition;

		private void Awake()
		{
			if (_targetGraphic != null)
			{
				_originalPosition = _targetGraphic.anchoredPosition;
			}

			OnPointerDownEvent += playDownFeedback;
			OnPointerUpEvent += playUpFeedback;
			OnClickEvent += playClickFeedback;
		}

		private void OnDestroy()
		{
			OnPointerDownEvent -= playDownFeedback;
			OnPointerUpEvent -= playUpFeedback;
			OnClickEvent -= playClickFeedback;
		}

		private void playDownFeedback()
		{
			if (_themeData == null || _targetGraphic == null) { return; }

			_targetGraphic.DOKill();
			_targetGraphic.DOAnchorPos(_originalPosition + _themeData.pressedOffset, _themeData.animationDuration).SetEase(Ease.OutQuad);
		}

		private void playUpFeedback()
		{
			if (_themeData == null || _targetGraphic == null) { return; }

			_targetGraphic.DOKill();
			_targetGraphic.DOAnchorPos(_originalPosition, _themeData.animationDuration).SetEase(Ease.OutBack);
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
	}
}
