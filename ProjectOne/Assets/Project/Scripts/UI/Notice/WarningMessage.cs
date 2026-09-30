using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 게임 내 제한 안내 (예: 퀘스트로 막힌 포탈 구역에 들어가 있는 동안 "포탈이 비활성화 되어있습니다").
	//
	// 시간으로 사라지지 않는다 — 띄우고 내리는 시점은 호출부가 정한다.
	public class WarningMessage : MonoBehaviour
	{
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private TMP_Text _text;
		// 텍스트가 이 너비를 넘으면 줄바꿈한다
		[SerializeField] private float _maxTextWidth = 600f;
		// 텍스트 바깥 여백 합계 (x: 좌우 합, y: 상하 합)
		[SerializeField] private Vector2 _padding = new Vector2(64f, 24f);

		private void Awake()
		{
			_canvasGroup.alpha = 0f;
		}

		// 이미 떠 있으면 문구만 바꾼다.
		public void Show(string message)
		{
			_text.text = message;
			fitToText(message);
			_canvasGroup.alpha = 1f;
		}

		// 한 줄 선호 너비를 최대 너비로 자르고, 그 너비에서 줄바꿈된 높이로 Text·루트(Bg) 크기를 맞춘다.
		private void fitToText(string message)
		{
			Vector2 singleLine = _text.GetPreferredValues(message);
			float width = Mathf.Min(singleLine.x, _maxTextWidth);
			float height = _text.GetPreferredValues(message, width, 0f).y;

			_text.rectTransform.sizeDelta = new Vector2(width, height);
			((RectTransform)transform).sizeDelta = new Vector2(width + _padding.x, height + _padding.y);
		}

		public void Hide()
		{
			_canvasGroup.alpha = 0f;
		}
	}
}
