using UnityEngine;
using UnityEngine.UI;
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
		[SerializeField] private RectTransform _targetGraphic;	// 스케일 연출 대상. 미지정 시 루트(transform) 사용

		[Header("Disabled Tint")]
		[SerializeField] private Graphic[] _disabledTintTargets;	// 비활성 시 어둡게 칠할 그래픽(본체 이미지 + Deco)
		[SerializeField] private Color _disabledColor = new Color(0.35f, 0.35f, 0.35f, 1f);

		private Vector3 _originalScale = Vector3.one;
		private Color[] _originalColors;

		public override bool interactable
		{
			get { return base.interactable; }
			set
			{
				base.interactable = value;
				refreshDisabledTint();
			}
		}

		private void Awake()
		{
			Transform target = _targetGraphic != null ? _targetGraphic : transform;
			_originalScale = target.localScale;

			cacheDisabledTint();
			refreshDisabledTint();

			OnPointerDownEvent += playDownFeedback;
			OnPointerUpEvent += playUpFeedback;
			OnClickEvent += playClickFeedback;
		}

		private void OnDestroy()
		{
			// 진행 중인 스케일 트윈이 파괴된 RectTransform에 접근하지 않도록 정리
			Transform target = _targetGraphic != null ? _targetGraphic : transform;
			target.DOKill();

			OnPointerDownEvent -= playDownFeedback;
			OnPointerUpEvent -= playUpFeedback;
			OnClickEvent -= playClickFeedback;
		}

		private void playDownFeedback()
		{
			if (_themeData == null) { return; }

			Transform target = _targetGraphic != null ? _targetGraphic : transform;
			Vector3 pressed = new Vector3(_originalScale.x * _themeData.pressedScale.x, _originalScale.y * _themeData.pressedScale.y, _originalScale.z);
			target.DOKill();
			target.DOScale(pressed, _themeData.animationDuration).SetEase(Ease.OutQuad);
		}

		private void playUpFeedback()
		{
			if (_themeData == null) { return; }

			Transform target = _targetGraphic != null ? _targetGraphic : transform;
			target.DOKill();
			target.DOScale(_originalScale, _themeData.animationDuration).SetEase(Ease.OutBack);
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

		private void cacheDisabledTint()
		{
			if (_disabledTintTargets == null) { return; }

			_originalColors = new Color[_disabledTintTargets.Length];
			for (int i = 0; i < _disabledTintTargets.Length; i++)
			{
				if (_disabledTintTargets[i] != null)
				{
					_originalColors[i] = _disabledTintTargets[i].color;
				}
			}
		}

		private void refreshDisabledTint()
		{
			if (_disabledTintTargets == null || _originalColors == null) { return; }

			for (int i = 0; i < _disabledTintTargets.Length; i++)
			{
				if (_disabledTintTargets[i] == null) { continue; }

				_disabledTintTargets[i].color = interactable ? _originalColors[i] : _disabledColor;
			}
		}
	}
}
