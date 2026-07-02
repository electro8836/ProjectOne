using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;
using TMPro;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 웨이브 정보 배너.
	// - 웨이브 시작 시 "Wave N / M"을 즉시 표시 → _showDuration 유지 후 _fadeDuration 동안 페이드아웃
	// - 다음 웨이브 대기 중에는 "N/M단계 방어 완료"(클리어 웨이브 / 최종 웨이브)를 항상 표시하고 스킵 버튼을 노출
	//   (시간 경과 자동 전환은 미사용 — 카운트다운 로직은 보존하되 사용하지 않는다)
	public class WaveInfoTitle : MonoBehaviour
	{
		[Header("참조")]
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private TMP_Text _titleText;
		[SerializeField] private UIButton _skipButton;

		[Header("연출")]
		[SerializeField] private float _showDuration = 3f;
		[SerializeField] private float _fadeDuration = 1f;

		[Header("등장 펀치(DOTween)")]
		[SerializeField] private Transform _popTarget;          // 미지정 시 자기 transform 사용
		[SerializeField] private float _punchStrength = 0.15f;  // 스케일 증가량 배율(피크 1.15배)
		[SerializeField] private float _punchDuration = 0.4f;

		private Action<WaveStateChangedEvent> _onWaveStateChanged;
		private Coroutine _fadeRoutine;
		private Coroutine _skipShowRoutine;
		private bool _isWaiting;
		private float _waitRemaining;
		private Vector3 _originalScale;

		private void Awake()
		{
			if (_popTarget == null)
			{
				_popTarget = transform;
			}

			_originalScale = _popTarget.localScale;

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
			_popTarget.DOKill();
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
				// 웨이브 클리어 — "N/M단계 방어 완료" 표시
				// 자동 카운트다운은 미사용이므로 _isWaiting을 켜지 않는다(Update 카운트다운 로직은 보존).
				stopFade();
				stopSkipShow();
				_isWaiting = false;
				_titleText.text = evt.CurrentWave + "/" + evt.TotalWaves + "단계 방어 완료";
				_canvasGroup.alpha = 1f;
				playPop();

				if (evt.CanSkip == true)
				{
					// 중간 웨이브 — 펀치 연출이 끝난 뒤 스킵 버튼 노출, 스킵까지 유지
					_skipShowRoutine = StartCoroutine(showSkipAfterPop());
				}
				else
				{
					// 마지막 웨이브 — 스킵 없이 유지 후 페이드아웃
					_fadeRoutine = StartCoroutine(showThenFade());
				}
			}
			else
			{
				// 웨이브 시작 — "N/M단계 시작" 표시 후 페이드아웃
				stopFade();
				stopSkipShow();
				_isWaiting = false;
				_titleText.text = evt.CurrentWave + " / " + evt.TotalWaves + "단계 시작";
				_canvasGroup.alpha = 1f;
				_fadeRoutine = StartCoroutine(showThenFade());
				playPop();
			}
		}

		// 배너 등장 시 톡 튀는 펀치 스케일 연출
		private void playPop()
		{
			_popTarget.DOKill();
			_popTarget.localScale = _originalScale;   // 중단된 펀치 잔여값 원복
			_popTarget.DOPunchScale(_originalScale * _punchStrength, _punchDuration);
		}

		private void onSkipClicked()
		{
			EventManager.Instance.Publish(new WaveSkipRequestedEvent());
		}

		// 펀치 연출이 끝난 뒤 스킵 버튼 노출
		private IEnumerator showSkipAfterPop()
		{
			yield return new WaitForSeconds(_punchDuration);

			_skipButton.gameObject.SetActive(true);
			_skipShowRoutine = null;
		}

		private void stopSkipShow()
		{
			if (_skipShowRoutine != null)
			{
				StopCoroutine(_skipShowRoutine);
				_skipShowRoutine = null;
			}

			_skipButton.gameObject.SetActive(false);
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
