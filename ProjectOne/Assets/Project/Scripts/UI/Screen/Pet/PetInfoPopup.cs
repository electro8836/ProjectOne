using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 펫 목록 팝업의 View(MVP). UIManager.ShowPetInfoPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 루트가 전체화면 stretch 라 팝업 캔버스에서 화면을 통째로 덮는다(네비게이션 바까지).
	// 창 스택에 들어가지 않으므로 닫기는 CloseWindowAsync 가 아니라 Close 로 한다 —
	// 스택을 건드리면 아래에 열려 있는 장비창이 닫힌다.
	// 표시와 입력 전달만 담당하고, 무엇을 어떤 색·문구로 깔지는 PetInfoPopupPresenter 가 정한다.
	public class PetInfoPopup : UIScreen, IView
	{
		[Header("목록")]
		[SerializeField] private TMP_Text _titleText;			// Top/TitleText
		[SerializeField] private RectTransform _gridParent;		// Bottom_ScrollRect/Viewport/Content/Grid
		[SerializeField] private PetInfoSlot _slotPrefab;		// UIPrefab_PetInfoSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		[Header("정렬")]
		[SerializeField] private UIButton _sortButton;	// Top/SortingButton
		[SerializeField] private TMP_Text _sortLabel;	// Top/SortingButton/Text

		[Header("닫기")]
		[SerializeField] private UIButton _returnButton;	// Return_Button

		public event Action<EDT.Pet> OnSlotClicked;
		public event Action OnSortClicked;
		public event Action OnReturnClicked;

		private readonly PetInfoPopupPresenter _presenter = new PetInfoPopupPresenter();

		private readonly List<PetInfoSlot> _slots = new List<PetInfoSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		private UniTaskCompletionSource _tcs;

		// Presenter 가 등급 색을 정해야 해서 열어 둔다 (EquipmentUI 가 SO 를 들고 있는 것과 같은 자리).
		public ItemGradeColorTable GradeColors
		{
			get { return _gradeColors; }
		}

		private void Awake()
		{
			_sortButton.OnClickEvent += onSortClicked;
			_returnButton.OnClickEvent += onReturnClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].OnClicked -= onSlotClicked;
			}

			_sortButton.OnClickEvent -= onSortClicked;
			_returnButton.OnClickEvent -= onReturnClicked;

			_presenter.Dispose();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			await _presenter.ShowAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public void SetTitle(int owned, int total)
		{
			_titleText.text = $"펫 ({owned} / {total})";
		}

		public void SetSortLabel(string label)
		{
			_sortLabel.text = label;
		}

		// 슬롯은 파괴하지 않고 재사용한다 — 정렬을 바꿀 때마다 Instantiate 하면 GC 가 튄다.
		public async UniTask RenderListAsync(IReadOnlyList<PetSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();

			for (int i = 0; i < data.Count; i++)
			{
				PetInfoSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private PetInfoSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			PetInfoSlot slot = Instantiate(_slotPrefab, _gridParent);
			slot.OnClicked += onSlotClicked;	// 생성 시 1회만 구독
			_slots.Add(slot);

			return slot;
		}

		private void onSlotClicked(PetInfoSlot sender)
		{
			if (OnSlotClicked != null)
			{
				OnSlotClicked.Invoke(sender.PetId);
			}
		}

		private void onSortClicked()
		{
			if (OnSortClicked != null)
			{
				OnSortClicked.Invoke();
			}
		}

		private void onReturnClicked()
		{
			if (OnReturnClicked != null)
			{
				OnReturnClicked.Invoke();
			}
		}
	}
}
