using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 로딩 화면(LoadingUI, UI Toolkit). 레이아웃은 UIToolkit/HUD/Loading/LoadingScreen.uxml.
	//
	// LoadingManager 가 UIManager 와 따로 띄우고, 모든 UI 위(정렬 999)에 덮어야 해서
	// 층에 붙지 않고 자기 UIDocument(PanelSettings_Loading)를 가진다 — 층 구조의 유일한 예외.
	// 진행률은 목표값을 받아 매 프레임 일정 속도로 따라가고, 닫을 때는 100% 로 맞춘 뒤 서서히 사라진다.
	[RequireComponent(typeof(UIDocument))]
	public class LoadingUI : UIScreen
	{
		[SerializeField] private float _fadeDuration = 0.25f;
		[SerializeField] private float _fillSpeed = 1.5f;	// 초당 게이지 채움 속도(0~1 기준)

		private VisualElement _root;
		private VisualElement _fill;
		private Label _progressText;
		private Label _phaseText;

		private float _current;
		private float _targetProgress;
		private int _shownPercent = -1;

		// 요소는 처음 쓸 때 찾는다 — 같은 오브젝트의 UIDocument 가 루트를 만든 뒤여야 한다.
		private bool ensureBound()
		{
			if (_fill != null)
			{
				return true;
			}

			UIDocument document = GetComponent<UIDocument>();
			if (document == null || document.rootVisualElement == null)
			{
				return false;
			}

			_root = document.rootVisualElement;
			_fill = _root.Q("progress-fill");
			_progressText = _root.Q<Label>("progress-text");
			_phaseText = _root.Q<Label>("phase-text");
			return _fill != null;
		}

		public void SetProgress(float target01)
		{
			_targetProgress = Mathf.Clamp01(target01);
		}

		public void SetPhase(string text)
		{
			if (ensureBound() == true)
			{
				_phaseText.text = text;
			}
		}

		private void Update()
		{
			if (ensureBound() == false)
			{
				return;
			}

			_current = Mathf.MoveTowards(_current, _targetProgress, _fillSpeed * Time.deltaTime);
			applyProgress();
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			if (ensureBound() == false)
			{
				return UniTask.CompletedTask;
			}

			_targetProgress = 0f;
			_current = 0f;
			applyProgress();
			_root.style.opacity = 1f;	// 페이드인 없이 즉시 표시
			return UniTask.CompletedTask;
		}

		public override async UniTask OnCloseAsync()
		{
			if (ensureBound() == false)
			{
				return;
			}

			_targetProgress = 1f;
			_current = 1f;
			applyProgress();
			await fadeOutAsync();
		}

		// 퍼센트가 바뀔 때만 글자를 다시 쓴다.
		private void applyProgress()
		{
			_fill.style.width = Length.Percent(_current * 100f);

			int percent = Mathf.RoundToInt(_current * 100f);
			if (percent != _shownPercent)
			{
				_shownPercent = percent;
				_progressText.text = percent + "%";
			}
		}

		private async UniTask fadeOutAsync()
		{
			float elapsed = 0f;
			while (elapsed < _fadeDuration)
			{
				elapsed += Time.deltaTime;
				_root.style.opacity = Mathf.Lerp(1f, 0f, elapsed / _fadeDuration);
				await UniTask.Yield(PlayerLoopTiming.Update);
			}

			_root.style.opacity = 0f;
		}
	}
}
