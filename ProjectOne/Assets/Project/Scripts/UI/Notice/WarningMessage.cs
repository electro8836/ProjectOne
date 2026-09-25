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

		private void Awake()
		{
			_canvasGroup.alpha = 0f;
		}

		// 이미 떠 있으면 문구만 바꾼다.
		public void Show(string message)
		{
			_text.text = message;
			_canvasGroup.alpha = 1f;
		}

		public void Hide()
		{
			_canvasGroup.alpha = 0f;
		}
	}
}
