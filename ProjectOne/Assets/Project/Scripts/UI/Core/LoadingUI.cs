using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectOne.UI
{
	// 로딩 화면 — 진행률 게이지 / 단계 텍스트 / 페이드 인·아웃.
	// 진행 값은 SetProgress 로 목표만 설정하고 Update 에서 부드럽게 이징한다(끊김 방지).
	public class LoadingUI : UIScreen
	{
		[Header("참조")]
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private Slider _progressBar;
		[SerializeField] private TMP_Text _progressText;
		[SerializeField] private TMP_Text _loadingText;

		[Header("연출")]
		[SerializeField] private float _fadeDuration = 0.25f;
		[SerializeField] private float _fillSpeed = 1.5f;   // 초당 게이지 채움 속도(0~1 기준)

		private float _targetProgress;

		// 진행 목표값 설정(0~1). 실제 게이지는 Update 에서 이징으로 따라간다.
		public void SetProgress(float target01)
		{
			_targetProgress = Mathf.Clamp01(target01);
		}

		// 단계 안내 문구 설정.
		public void SetPhase(string text)
		{
			if (_loadingText != null)
			{
				_loadingText.text = text;
			}
		}

		private void Update()
		{
			float current = Mathf.MoveTowards(_progressBar.value, _targetProgress, _fillSpeed * Time.deltaTime);
			_progressBar.value = current;
			_progressText.text = Mathf.RoundToInt(current * 100f) + "%";
		}

		public override async UniTask OnOpenAsync(CancellationToken ct)
		{
			_targetProgress = 0f;
			_progressBar.value = 0f;
			_progressText.text = "0%";
			await fadeAsync(0f, 1f, ct);
		}

		public override async UniTask OnCloseAsync()
		{
			// 닫기 직전 게이지를 100% 로 스냅
			_targetProgress = 1f;
			_progressBar.value = 1f;
			_progressText.text = "100%";
			await fadeAsync(1f, 0f, CancellationToken.None);
		}

		private async UniTask fadeAsync(float from, float to, CancellationToken ct)
		{
			_canvasGroup.alpha = from;
			float elapsed = 0f;
			while (elapsed < _fadeDuration)
			{
				elapsed += Time.deltaTime;
				_canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / _fadeDuration);
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}

			_canvasGroup.alpha = to;
		}
	}
}
