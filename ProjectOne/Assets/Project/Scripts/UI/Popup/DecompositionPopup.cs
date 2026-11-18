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
	// 분해할 등급(체크) · 품질 상한(체크 + 슬라이더) · 자동 분해(체크)를 고르고 분해한다.
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
				Button check = createCheck(ItemGradeNames.Get(grade));
				check.userData = grade;
				check.RegisterCallback<ClickEvent>(onGradeCheckClicked);
				check.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
				if (_gradeColors != null)
				{
					check.Q<Label>().style.color = _gradeColors.Get(grade).text;
				}

				grades.Add(check);
				_gradeChecks.Add(check);
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
					_gradeChecks[i].EnableInClassList("check--on", value);
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
			_qualityTitle.text = "아이템의 품질 <color=#7BE07A>" + EquipmentQuality.Format(quality) + "%</color> 이하 분해";
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

		// 체크 상자 + 글자 버튼 (Theme.uss 의 .check).
		private static Button createCheck(string text)
		{
			Button check = new Button();
			check.AddToClassList("check");

			VisualElement box = new VisualElement();
			box.AddToClassList("check__box");
			box.pickingMode = PickingMode.Ignore;
			check.Add(box);

			Label label = new Label(text);
			label.AddToClassList("check__label");
			label.pickingMode = PickingMode.Ignore;
			check.Add(label);
			return check;
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
