using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 상자 확률표 팝업의 View(MVP). UIManager.ShowBoxRewardPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 등급마다 탭 하나를 쓰고, 그 등급으로 나올 수 있는 아이템을 확률과 함께 깐다.
	// 어떤 등급이 존재하는지·무엇을 깔지는 BoxRewardPresenter 가 정한다.
	public class BoxRewardPopup : UIScreen, IView
	{
		[Header("탭")]
		[SerializeField] private TabGroup _tabGroup;			// Frame/Bg/Top/TabButtons
		[SerializeField] private UITabButton[] _tabButtons;		// TabButton_01 ~ 06 (Hierarchy 순서와 일대일)

		[Header("목록")]
		[SerializeField] private ScrollRect _scrollRect;		// Frame/ScrollRect
		[SerializeField] private RectTransform _grid;			// Frame/ScrollRect/Viewport/Content/Grid
		[SerializeField] private ItemPoolSlot _slotPrefab;		// UIPrefab_ItemPoolSlot
		[SerializeField] private ItemSlot _itemSlotPrefab;		// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;			// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		public event Action<int> OnTabSelected;	// 탭 인덱스

		private readonly BoxRewardPresenter _presenter = new BoxRewardPresenter();
		private readonly List<ItemPoolSlot> _slots = new List<ItemPoolSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private CanvasGroup _canvasGroup;
		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			// 첫 렌더가 끝나기 전 빈 칸이 비치지 않도록 숨겨 두고 시작한다.
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			setVisible(false);

			if (_tabGroup != null)
			{
				_tabGroup.OnTabChanged += onTabChanged;
			}

			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			if (_tabGroup != null)
			{
				_tabGroup.OnTabChanged -= onTabChanged;
			}

			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;

			_presenter.Dispose();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(int rewardGroupId, CancellationToken ct)
		{
			await _presenter.ShowAsync(rewardGroupId, ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		// 나올 수 있는 등급만 앞에서부터 채우고 남는 탭은 끈다.
		public void SetGrades(IReadOnlyList<ItemGradeType> grades)
		{
			if (_tabButtons == null)
			{
				return;
			}

			for (int i = 0; i < _tabButtons.Length; i++)
			{
				UITabButton button = _tabButtons[i];
				if (button == null)
				{
					continue;
				}

				bool used = i < grades.Count;
				button.gameObject.SetActive(used);

				if (used == true)
				{
					button.SetLabel(ItemGradeNames.Get(grades[i]));
				}
			}
		}

		public void SelectTab(int index)
		{
			if (_tabGroup != null)
			{
				_tabGroup.Select(index);
			}
		}

		public async UniTask RenderAsync(IReadOnlyList<RewardChance> chances, CancellationToken ct)
		{
			_bindTasks.Clear();

			for (int i = 0; i < chances.Count; i++)
			{
				ItemPoolSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(chances[i], _itemSlotPrefab, _gradeColors, ct));
			}

			for (int i = chances.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();

			if (_scrollRect != null)
			{
				_scrollRect.verticalNormalizedPosition = 1f;
			}
		}

		public void Reveal()
		{
			setVisible(true);
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private ItemPoolSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			ItemPoolSlot slot = Instantiate(_slotPrefab, _grid);
			_slots.Add(slot);
			return slot;
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		private void onTabChanged(int index)
		{
			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke(index);
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
