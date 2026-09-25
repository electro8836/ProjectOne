using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 유지 후 사라지는 한 줄 알림 — 경고(Alert)·시스템(System) 메시지 공용.
	//
	// 여러 개가 겹쳐 오면 대기열에 쌓아 두고, 앞의 것이 완전히 사라진 뒤 다음 것을 띄운다.
	// 유지 시간만 다르므로(경고 3초 · 시스템 5초) 프리펩에서 _holdSeconds 로 정한다.
	//
	// **루트는 끄지 않고 CanvasGroup 알파로 숨긴다.** 루트를 끄면 코루틴이 멈춘다.
	public class NoticeMessage : MonoBehaviour
	{
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private TMP_Text _text;

		[Header("연출")]
		[SerializeField] private float _holdSeconds = 3f;
		[SerializeField] private float _fadeSeconds = 0.5f;

		private readonly Queue<string> _pending = new Queue<string>();
		private Coroutine _playRoutine;

		private void Awake()
		{
			_canvasGroup.alpha = 0f;
		}

		// 대기열에 넣는다. 재생 중이 아니면 바로 띄운다.
		public void Show(string message)
		{
			_pending.Enqueue(message);

			if (_playRoutine == null)
			{
				_playRoutine = StartCoroutine(playQueue());
			}
		}

		private IEnumerator playQueue()
		{
			while (_pending.Count > 0)
			{
				_text.text = _pending.Dequeue();
				_canvasGroup.alpha = 1f;

				yield return new WaitForSecondsRealtime(_holdSeconds);

				float elapsed = 0f;
				while (elapsed < _fadeSeconds)
				{
					elapsed += Time.unscaledDeltaTime;
					_canvasGroup.alpha = Mathf.Clamp01(1f - elapsed / _fadeSeconds);
					yield return null;
				}

				_canvasGroup.alpha = 0f;
			}

			_playRoutine = null;
		}
	}
}
