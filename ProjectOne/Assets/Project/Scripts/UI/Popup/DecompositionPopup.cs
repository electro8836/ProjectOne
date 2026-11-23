using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 일괄 분해 팝업(UIPrefab_DecompositionPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Decomposition/DecompositionPopup.uxml.
	// 분해할 등급(칩 — 켜면 등급색 + 체크) · 품질 상한(체크 + 막대) · 자동 분해(스위치)를 고르고 분해한다.
	// 선택 상태는 DecompositionPopupPresenter 가 소유하고 View 는 그리기만 한다.
	public class DecompositionPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action<ItemGradeType> OnGradeClicked;
		public event Action OnQualityCheckClicked;
		public event Action<int> OnQualityChanged;
		public event Action OnAutoCheckClicked;
		public event Action OnDecompositionClicked;
		public event Action OnExitClicked;

		private readonly DecompositionPopupPresenter _presenter = new DecompositionPopupPresenter();
		private readonly List<Button> _gradeChecks = new List<Button>();
		private readonly List<Label> _gradeLabels = new List<Label>();

		private Button _qualityCheck;
		private SliderInt _qualitySlider;
		private Label _qualityTitle;
		private Button _autoCheck;
		private UniTaskCompletionSource<bool> _tcs;

		protected override void OnBind(VisualElement root)
		{
			VisualElement grades = root.Q("grades");
			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				ItemGradeType grade = (ItemGradeType)g;
				Button chip = createChip(ItemGradeNames.Get(grade));
				chip.userData = grade;
				chip.RegisterCallback<ClickEvent>(onGradeCheckClicked);
				chip.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

				grades.Add(chip);
				_gradeChecks.Add(chip);
				_gradeLabels.Add(chip.Q<Label>());
				applyChip(_gradeChecks.Count - 1, false);
			}

			_qualityCheck = root.Q<Button>("quality-check");
			_qualityCheck.clicked += onQualityCheckClicked;
			_qualityTitle = root.Q<Label>("quality-title");
			_qualitySlider = root.Q<SliderInt>("quality-slider");
			_qualitySlider.lowValue = 0;
			_qualitySlider.highValue = EquipmentQuality.Max;
			_qualitySlider.RegisterValueChangedCallback(onQualitySliderChanged);

			_autoCheck = root.Q<Button>("auto-check");
			_autoCheck.clicked += onAutoCheckClicked;

			root.Q<Button>("decompose").clicked += onDecompositionClicked;
			root.Q<Button>("close").clicked += onExitClicked;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);

			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_presenter.Dispose();
			}

			base.OnDestroy();
		}

		public UniTask ShowAsync(CancellationToken ct)
		{
			PlayPopupOpen();
			return _presenter.ShowAsync(ct);
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		public void SetGradeChecked(ItemGradeType grade, bool value)
		{
			for (int i = 0; i < _gradeChecks.Count; i++)
			{
				if ((ItemGradeType)_gradeChecks[i].userData == grade)
				{
					applyChip(i, value);
					return;
				}
			}
		}

		public void SetQualityChecked(bool value)
		{
			_qualityCheck.EnableInClassList("check--on", value);
		}

		public void SetQuality(int quality)
		{
			_qualitySlider.SetValueWithoutNotify(quality);
			_qualityTitle.text = "품질 <color=#7BE07A>" + EquipmentQuality.Format(quality) + "%</color> 이하 분해";
		}

		public void SetAutoChecked(bool value)
		{
			_autoCheck.EnableInClassList("check--on", value);
		}

		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		// 등급 칩 — 체크 표시 + 등급 이름 (Decomposition.uss 의 .decompose-chip).
		private static Button createChip(string text)
		{
			Button chip = new Button();
			chip.AddToClassList("decompose-chip");

			VisualElement mark = new VisualElement();
			mark.AddToClassList("decompose-chip__mark");
			mark.pickingMode = PickingMode.Ignore;
			chip.Add(mark);

			Label label = new Label(text);
			label.AddToClassList("decompose-chip__label");
			label.pickingMode = PickingMode.Ignore;
			chip.Add(label);
			return chip;
		}

		// 켜면 등급색 면·테두리 + 흰 글자, 끄면 USS 의 어두운 칩에 등급색 글자.
		private void applyChip(int index, bool on)
		{
			Button chip = _gradeChecks[index];
			chip.EnableInClassList("decompose-chip--on", on);
			if (_gradeColors == null)
			{
				return;
			}

			ItemGradeColorTable.GradeColor colors = _gradeColors.Get((ItemGradeType)chip.userData);
			chip.style.backgroundColor = on ? new StyleColor(colors.bg) : new StyleColor(StyleKeyword.Null);
			chip.style.borderTopColor = on ? new StyleColor(colors.border) : new StyleColor(StyleKeyword.Null);
			chip.style.borderBottomColor = chip.style.borderTopColor;
			chip.style.borderLeftColor = chip.style.borderTopColor;
			chip.style.borderRightColor = chip.style.borderTopColor;
			_gradeLabels[index].style.color = on ? Color.white : colors.text;
		}

		private void onGradeCheckClicked(ClickEvent evt)
		{
			ItemGradeType grade = (ItemGradeType)((VisualElement)evt.currentTarget).userData;
			if (OnGradeClicked != null) { OnGradeClicked.Invoke(grade); }
		}

		private void onQualityCheckClicked()
		{
			if (OnQualityCheckClicked != null) { OnQualityCheckClicked.Invoke(); }
		}

		private void onQualitySliderChanged(ChangeEvent<int> evt)
		{
			if (OnQualityChanged != null) { OnQualityChanged.Invoke(evt.newValue); }
		}

		private void onAutoCheckClicked()
		{
			if (OnAutoCheckClicked != null) { OnAutoCheckClicked.Invoke(); }
		}

		private void onDecompositionClicked()
		{
			if (OnDecompositionClicked != null) { OnDecompositionClicked.Invoke(); }
		}

		private void onExitClicked()
		{
			if (OnExitClicked != null) { OnExitClicked.Invoke(); }
		}

		private void onDimClicked(ClickEvent evt)
		{
			onExitClicked();
		}
	}
}
