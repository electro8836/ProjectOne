using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 일괄 분해 팝업의 View(MVP). UIManager.ShowDecompositionPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 조건 표시와 입력 전달만 담당하고, 조건 보관·대상 선정·분해 요청은 DecompositionPopupPresenter 가 한다.
	public class DecompositionPopup : UIScreen, IView
	{
		[Header("등급")]
		// Grid 에 놓아 둔 슬롯 1칸. 이것을 원본 삼아 등급 수만큼 늘린다.
		[SerializeField] private DecompositionGradeSlot _gradeSlotTemplate;	// Frame/Grade/.../Grid/UIPrefb_DecompositionGradeSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;			// 등급 색상 SO

		[Header("품질")]
		[SerializeField] private UIButton _qualityCheckButton;	// Frame/Quality/CheckButton
		[SerializeField] private GameObject _qualityCheckImage;	// Frame/Quality/CheckButton/Image
		[SerializeField] private Slider _qualitySlider;			// Frame/Quality/Slider
		[SerializeField] private TMP_Text _qualityTitle;		// Frame/Quality/Title

		[Header("자동 분해")]
		[SerializeField] private UIButton _autoCheckButton;		// Frame/Auto/CheckButton
		[SerializeField] private GameObject _autoCheckImage;	// Frame/Auto/CheckButton/Image

		[Header("버튼")]
		[SerializeField] private UIButton _decompositionButton;	// Frame/Bottom/DecompositionButton

		// 닫기는 ExitButton 과 Dim 두 경로다.
		[SerializeField] private UIButton _exitButton;	// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;	// Dim

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<ItemGradeType> OnGradeClicked;
		public event Action OnQualityCheckClicked;
		public event Action<int> OnQualityChanged;
		public event Action OnAutoCheckClicked;
		public event Action OnDecompositionClicked;
		public event Action OnExitClicked;

		private readonly DecompositionPopupPresenter _presenter = new DecompositionPopupPresenter();
		private readonly List<DecompositionGradeSlot> _gradeSlots = new List<DecompositionGradeSlot>();
		private UniTaskCompletionSource<bool> _tcs;

		private void Awake()
		{
			buildGradeSlots();

			// 품질은 0.1% 단위의 정수다(0.0 ~ 100.0) — 프리펩 값과 무관하게 여기서 축을 맞춘다.
			_qualitySlider.wholeNumbers = true;
			_qualitySlider.minValue = 0f;
			_qualitySlider.maxValue = EquipmentQuality.Max;

			_qualityCheckButton.OnClickEvent += onQualityCheckClicked;
			_qualitySlider.onValueChanged.AddListener(onQualityChanged);
			_autoCheckButton.OnClickEvent += onAutoCheckClicked;
			_decompositionButton.OnClickEvent += onDecompositionClicked;
			_exitButton.OnClickEvent += onExitClicked;
			_dimButton.OnClickEvent += onExitClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			for (int i = 0; i < _gradeSlots.Count; i++)
			{
				_gradeSlots[i].OnClicked -= onGradeClicked;
			}

			_qualityCheckButton.OnClickEvent -= onQualityCheckClicked;
			_qualitySlider.onValueChanged.RemoveListener(onQualityChanged);
			_autoCheckButton.OnClickEvent -= onAutoCheckClicked;
			_decompositionButton.OnClickEvent -= onDecompositionClicked;
			_exitButton.OnClickEvent -= onExitClicked;
			_dimButton.OnClickEvent -= onExitClicked;
		}

		// UIManager 가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		public UniTask ShowAsync(CancellationToken ct)
		{
			return _presenter.ShowAsync(ct);
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		public void SetGradeChecked(ItemGradeType grade, bool value)
		{
			for (int i = 0; i < _gradeSlots.Count; i++)
			{
				if (_gradeSlots[i].Grade == grade)
				{
					_gradeSlots[i].SetChecked(value);
					return;
				}
			}
		}

		public void SetQualityChecked(bool value)
		{
			_qualityCheckImage.SetActive(value);
		}

		// 슬라이더 위치와 안내 문구를 함께 맞춘다. 슬라이더는 알림 없이 옮긴다 — 표시가 입력으로 되돌아오지 않게.
		public void SetQuality(int quality)
		{
			_qualitySlider.SetValueWithoutNotify(quality);
			_qualityTitle.text = "아이템의 품질 <color=green>" + EquipmentQuality.Format(quality) + "%</color> 이하 분해";
		}

		public void SetAutoChecked(bool value)
		{
			_autoCheckImage.SetActive(value);
		}

		// 닫힘 대기 — Presenter 의 ShowAsync 가 마지막에 await 한다.
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

		// ── 내부 ──────────────────────────────────────────────────────────

		// Normal ~ Mythic 한 칸씩. 첫 칸은 놓여 있던 원본을 그대로 쓰고 나머지는 복제한다.
		private void buildGradeSlots()
		{
			Transform parent = _gradeSlotTemplate.transform.parent;
			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				DecompositionGradeSlot slot = (_gradeSlots.Count == 0) ? _gradeSlotTemplate : Instantiate(_gradeSlotTemplate, parent);
				slot.Bind((ItemGradeType)g, _gradeColors);
				slot.OnClicked += onGradeClicked;
				_gradeSlots.Add(slot);
			}
		}

		private void onGradeClicked(ItemGradeType grade)
		{
			if (OnGradeClicked != null) { OnGradeClicked.Invoke(grade); }
		}

		private void onQualityCheckClicked()
		{
			if (OnQualityCheckClicked != null) { OnQualityCheckClicked.Invoke(); }
		}

		private void onQualityChanged(float value)
		{
			if (OnQualityChanged != null) { OnQualityChanged.Invoke(Mathf.RoundToInt(value)); }
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
	}
}
