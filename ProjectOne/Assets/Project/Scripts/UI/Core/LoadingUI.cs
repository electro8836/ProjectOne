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
	// 게이지 끝의 빛 두 겹은 크기·밝기를 서로 다른 박자로 오르내려 일렁이게 한다.
	[RequireComponent(typeof(UIDocument))]
	public class LoadingUI : UIScreen
	{
		[SerializeField] private float _fadeDuration = 0.25f;
		[SerializeField] private float _fillSpeed = 1.5f;	// 초당 게이지 채움 속도(0~1 기준)

		private VisualElement _root;
		private VisualElement _fill;
		private VisualElement _glowWide;
		private VisualElement _glowCore;
		private Label _progressText;
		private Label _phaseText;

		private float _glowTime;
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
			_glowWide = _root.Q("glow-wide");
			_glowCore = _root.Q("glow-core");
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
			applyGlow();
		}

		// 넓은 번짐은 느리게 커졌다 줄고, 작은 흰빛은 조금 빠르게 밝아졌다 어두워진다 — 박자가 달라 일렁여 보인다.
		private void applyGlow()
		{
			if (_glowWide == null || _glowCore == null)
			{
				return;
			}

			_glowTime += Time.deltaTime;

			float wide = 1f + 0.14f * Mathf.Sin(_glowTime * 3.1f);
			_glowWide.style.scale = new Scale(new Vector3(wide, wide, 1f));
			_glowWide.style.opacity = 0.75f + 0.25f * Mathf.Sin(_glowTime * 2.3f + 1f);

			float core = 1f + 0.08f * Mathf.Sin(_glowTime * 4.7f + 2f);
			_glowCore.style.scale = new Scale(new Vector3(core, core, 1f));
			_glowCore.style.opacity = 0.7f + 0.3f * Mathf.Sin(_glowTime * 5.3f);
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
