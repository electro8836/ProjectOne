using System;
using System.Collections;
using UnityEngine;
using TMPro;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 웨이브 정보 배너.
	// - 웨이브 시작 시 "Wave N / M"을 즉시 표시 → _showDuration 유지 후 _fadeDuration 동안 페이드아웃
	// - 다음 웨이브 대기 중에는 "Next Wave Ns" 카운트다운을 항상 표시하고 스킵 버튼을 노출
	public class WaveInfoTitle : MonoBehaviour
	{
		[Header("참조")]
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private TMP_Text _titleText;
		[SerializeField] private UIButton _skipButton;

		[Header("연출")]
		[SerializeField] private float _showDuration = 3f;
		[SerializeField] private float _fadeDuration = 1f;

		private Action<WaveStateChangedEvent> _onWaveStateChanged;
		private Coroutine _fadeRoutine;
		private bool _isWaiting;
		private float _waitRemaining;

		private void Awake()
		{
			_onWaveStateChanged = onWaveStateChanged;
			EventManager.Instance.Subscribe<WaveStateChangedEvent>(_onWaveStateChanged);
			_skipButton.OnClickEvent += onSkipClicked;

			_canvasGroup.alpha = 0f;
			_skipButton.gameObject.SetActive(false);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<WaveStateChangedEvent>(_onWaveStateChanged);
			_skipButton.OnClickEvent -= onSkipClicked;
		}

		private void Update()
		{
			if (_isWaiting == false)
			{
				return;
			}

			_waitRemaining -= Time.deltaTime;
			if (_waitRemaining < 0f)
			{
				_waitRemaining = 0f;
			}

			int remaining = Mathf.CeilToInt(_waitRemaining);
			_titleText.text = "Next Wave " + remaining + "s";
		}

		private void onWaveStateChanged(WaveStateChangedEvent evt)
		{
			if (evt.IsWaiting == true)
			{
				// 다음 웨이브 대기 — 카운트다운 + 스킵 버튼, 항상 표시
				stopFade();
				_isWaiting = true;
				_waitRemaining = evt.WaitSeconds;
				_canvasGroup.alpha = 1f;
				_skipButton.gameObject.SetActive(true);
			}
			else
			{
				// 웨이브 시작 — "Wave N / M" 표시 후 페이드아웃
				stopFade();
				_isWaiting = false;
				_skipButton.gameObject.SetActive(false);
				_titleText.text = "Wave " + evt.CurrentWave + " / " + evt.TotalWaves;
				_canvasGroup.alpha = 1f;
				_fadeRoutine = StartCoroutine(showThenFade());
			}
		}

		private void onSkipClicked()
		{
			EventManager.Instance.Publish(new WaveSkipRequestedEvent());
		}

		private IEnumerator showThenFade()
		{
			yield return new WaitForSeconds(_showDuration);

			float elapsed = 0f;
			while (elapsed < _fadeDuration)
			{
				elapsed += Time.deltaTime;
				_canvasGroup.alpha = Mathf.Clamp01(1f - elapsed / _fadeDuration);
				yield return null;
			}

			_canvasGroup.alpha = 0f;
			_fadeRoutine = null;
		}

		private void stopFade()
		{
			if (_fadeRoutine != null)
			{
				StopCoroutine(_fadeRoutine);
				_fadeRoutine = null;
			}
		}
	}
}
