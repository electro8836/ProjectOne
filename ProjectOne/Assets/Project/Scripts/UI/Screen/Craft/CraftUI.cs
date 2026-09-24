using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
using TMPro;
using ProjectOne.Items;
using ProjectOne.Upgrade;

namespace ProjectOne.UI
{
	// 제작 화면의 View(MVP). 네비게이션 바의 Craft 탭으로 열린다 (UIPrefab_CraftInfo).
	// TabMenu_Side 로 강화·승급·전이 모드를 바꾸고, 하단 그리드에서 고른 장비를 모드별 등록 칸에 올린다.
	// 표시(슬롯 풀/등록 칸/비용)와 입력 전달만 담당하고, 등록·실행 결정은 CraftPresenter 가 한다.
	public class CraftUI : UIScreen, IView
	{
		[Header("탭")]
		[SerializeField] private TabGroup _modeTabs;	// TabMenu_Side (강화/승급/전이)
		[SerializeField] private TabGroup _filterTabs;	// Bottom/TabMenu_Middle (전체/무기/방어구/장신구/유물)

		[Header("리스트")]
		[SerializeField] private RectTransform _gridParent;			// Bottom/ScrollRect/Veiwport/Content/Grid
		[SerializeField] private ItemSlot _slotPrefab;				// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO
		[SerializeField] private CurrencyDisplaySlot _costSlotPrefab;	// UIPrefab_CurrencyDisplaySlot

		[Header("모드 패널")]
		[SerializeField] private ModePanel _enhancePanel;	// Top/Enhance
		[SerializeField] private ModePanel _promotePanel;	// Top/Promote
		[SerializeField] private ModePanel _transferPanel;	// Top/Transfer

		[Header("등록 칸")]
		[SerializeField] private RegisterSlotView[] _registerSlots;	// ItemRoot, PrevItemRoot, NextItemRoot, SourceItemRoot, TargetItemRoot

		[Header("닫기")]
		[SerializeField] private UIButton _homeButton;	// Top/HomeButton

		[Header("정렬")]
		[SerializeField] private UIButton _sortButton;	// Button_Sorting
		[SerializeField] private TMP_Text _sortLabel;	// Button_Sorting/Text (TMP)

		// 실행 성공 강조 — 커졌다가 제자리로 줄어든다.
		private const float PopScale = 1.3f;
		private const float PopUpDuration = 0.15f;
		private const float PopDownDuration = 0.25f;

		[Serializable]
		private class ModePanel
		{
			public GameObject root;					// Enhance / Promote / Transfer
			public UIButton actionButton;			// EnhanceButton / PromoteButton / TransferButton
			public UIButton cancelButton;			// CancelButton
			public GameObject requirementText;		// Requirement/Text
			public RectTransform costGrid;			// Requirement/CurrencyGrid
			public TMP_Text disableText;			// Requirement/DisableText — 전이는 비워 둔다
			[NonSerialized] public List<CurrencyDisplaySlot> costSlots;
		}

		[Serializable]
		private class RegisterSlotView
		{
			public CraftSlotRoot type;
			public Transform root;	// XXXItemRoot 컨테이너
			[NonSerialized] public ItemSlot instance;
		}

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnModeSelected;
		public event Action<int> OnFilterSelected;
		public event Action<long> OnGridSlotClicked;
		public event Action<CraftSlotRoot> OnRegisteredSlotClicked;
		public event Action OnActionClicked;
		public event Action OnCancelClicked;
		public event Action OnHomeClicked;
		public event Action OnSortClicked;

		private readonly CraftPresenter _presenter = new CraftPresenter();

		private readonly List<ItemSlot> _slots = new List<ItemSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		private void Awake()
		{
			_modeTabs.OnTabChanged += onModeChanged;
			_filterTabs.OnTabChanged += onFilterChanged;
			_homeButton.OnClickEvent += onHomeClicked;
			_sortButton.OnClickEvent += onSortClicked;
			bindPanelButtons(_enhancePanel);
			bindPanelButtons(_promotePanel);
			bindPanelButtons(_transferPanel);

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			killRegisterTweens();

			_presenter.Dispose();

			_modeTabs.OnTabChanged -= onModeChanged;
			_filterTabs.OnTabChanged -= onFilterChanged;
			_homeButton.OnClickEvent -= onHomeClicked;
			_sortButton.OnClickEvent -= onSortClicked;
			unbindPanelButtons(_enhancePanel);
			unbindPanelButtons(_promotePanel);
			unbindPanelButtons(_transferPanel);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		public override UniTask OnCloseAsync()
		{
			return _presenter.OnCloseAsync();
		}

		// 외부(장비 정보 팝업)에서 모드와 등록 장비를 지정해 연다. OnOpenAsync 뒤에 호출해야 한다.
		public void Preselect(CraftMode mode, long uid)
		{
			_presenter.Preselect(mode, uid);
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		// MonoBehaviour 의 파괴 토큰을 Presenter 에 제공(연속 rebuild 의 취소 기준).
		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// 탭만 선택(OnTabChanged 를 발행하지 않음 — 초기 표시용).
		public void SelectModeTab(int index)
		{
			_modeTabs.Select(index);
		}

		public void SelectFilterTab(int index)
		{
			_filterTabs.Select(index);
		}

		// 현재 정렬 기준을 버튼 라벨에 표시한다.
		public void SetSortLabel(string label)
		{
			_sortLabel.text = label;
		}

		// 해당 모드 패널만 켠다.
		public void ShowMode(CraftMode mode)
		{
			_enhancePanel.root.SetActive(mode == CraftMode.Enhance);
			_promotePanel.root.SetActive(mode == CraftMode.Promote);
			_transferPanel.root.SetActive(mode == CraftMode.Transfer);
		}

		// 그리드 슬롯 렌더 — 데이터 개수만큼 슬롯을 켜 바인딩, 남는 슬롯은 비활성화(풀 재사용).
		public async UniTask RenderGridAsync(IReadOnlyList<CraftSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				ItemSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindEquipmentAsync(data[i].instance, data[i].equipped, _gradeColors, ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 등록 칸 렌더 — instance 가 null 이면 비운다.
		// 빈 칸 프레임(ItemFrame_Empty)은 끄지 않는다 — 그 위에 슬롯을 얹어 겹쳐 보이게 한다 (EquipmentUI 장착 칸과 같은 방식).
		public async UniTask RenderRegisteredAsync(CraftSlotRoot type, EquipmentInstance instance, CancellationToken ct)
		{
			RegisterSlotView slotView = findRegisterSlot(type);
			if (slotView == null)
			{
				return;
			}

			if (instance == null)
			{
				clearRegisterSlot(slotView);
				return;
			}

			if (slotView.instance == null)
			{
				slotView.instance = Instantiate(_slotPrefab, slotView.root);
				slotView.instance.StretchToParent();
				slotView.instance.transform.SetAsLastSibling();
				slotView.instance.OnClicked += onRegisteredSlotClicked;
			}

			// 등록 칸에서는 장착 여부를 보여주지 않는다 — 그리드에서 이미 확인하고 올린 것이다.
			await slotView.instance.BindEquipmentAsync(instance, false, _gradeColors, ct);
		}

		// 비용 영역 렌더.
		//   visible 이 false 면 (등록된 것이 없음) 안내·비용·불가 문구를 모두 숨긴다.
		//   disableMessage 가 있으면 불가 문구만 보이고 안내·비용은 숨긴다.
		//   그 외에는 안내·비용을 보이고 비용 슬롯을 채운다.
		public async UniTask RenderRequirementAsync(CraftMode mode, bool visible, IReadOnlyList<UpgradeCost> costs, string disableMessage, CancellationToken ct)
		{
			ModePanel panel = getPanel(mode);
			bool disabled = visible == true && string.IsNullOrEmpty(disableMessage) == false;
			bool showCosts = visible == true && disabled == false;

			if (panel.disableText != null)
			{
				panel.disableText.gameObject.SetActive(disabled);
				if (disabled == true)
				{
					panel.disableText.text = disableMessage;
				}
			}

			panel.requirementText.SetActive(showCosts);
			panel.costGrid.gameObject.SetActive(showCosts);

			if (showCosts == false)
			{
				return;
			}

			_bindTasks.Clear();
			for (int i = 0; i < costs.Count; i++)
			{
				CurrencyDisplaySlot slot = getOrCreateCostSlot(panel, i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(costs[i], ct));
			}

			for (int i = costs.Count; i < panel.costSlots.Count; i++)
			{
				panel.costSlots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		public void SetActionInteractable(CraftMode mode, bool interactable)
		{
			getPanel(mode).actionButton.interactable = interactable;
		}

		public void SetCancelVisible(CraftMode mode, bool visible)
		{
			getPanel(mode).cancelButton.gameObject.SetActive(visible);
		}

		// 실행 성공 강조. 대상은 XXXItemRoot 컨테이너다 — 안쪽 ItemSlot 은 비울 때 파괴되므로 컨테이너를 키운다.
		public void PlayPop(CraftSlotRoot type)
		{
			RegisterSlotView slotView = findRegisterSlot(type);
			if (slotView == null)
			{
				return;
			}

			Transform root = slotView.root;
			root.DOKill();
			root.localScale = Vector3.one;

			Sequence sequence = DOTween.Sequence();
			sequence.Append(root.DOScale(Vector3.one * PopScale, PopUpDuration).SetEase(Ease.OutQuad));
			sequence.Append(root.DOScale(Vector3.one, PopDownDuration).SetEase(Ease.OutBack));
			sequence.SetTarget(root);
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onModeChanged(int index)
		{
			if (OnModeSelected != null) { OnModeSelected.Invoke(index); }
		}

		private void onFilterChanged(int index)
		{
			if (OnFilterSelected != null) { OnFilterSelected.Invoke(index); }
		}

		private void onHomeClicked()
		{
			if (OnHomeClicked != null) { OnHomeClicked.Invoke(); }
		}

		private void onSortClicked()
		{
			if (OnSortClicked != null) { OnSortClicked.Invoke(); }
		}

		// 세 패널의 실행·취소 버튼은 모드와 무관하게 같은 이벤트로 올린다 — 어느 모드인지는 Presenter 가 안다.
		private void onActionClicked()
		{
			if (OnActionClicked != null) { OnActionClicked.Invoke(); }
		}

		private void onCancelClicked()
		{
			if (OnCancelClicked != null) { OnCancelClicked.Invoke(); }
		}

		private void onGridSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			if (OnGridSlotClicked != null) { OnGridSlotClicked.Invoke(uid); }
		}

		// 등록 칸 슬롯 클릭 — 어느 칸인지(type)만 올린다. 칸에 무엇이 들어 있는지는 Presenter 가 안다.
		private void onRegisteredSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			if (_registerSlots == null)
			{
				return;
			}

			for (int i = 0; i < _registerSlots.Length; i++)
			{
				if (_registerSlots[i].instance == sender)
				{
					if (OnRegisteredSlotClicked != null) { OnRegisteredSlotClicked.Invoke(_registerSlots[i].type); }
					return;
				}
			}
		}

		private void bindPanelButtons(ModePanel panel)
		{
			panel.actionButton.OnClickEvent += onActionClicked;
			panel.cancelButton.OnClickEvent += onCancelClicked;
		}

		private void unbindPanelButtons(ModePanel panel)
		{
			panel.actionButton.OnClickEvent -= onActionClicked;
			panel.cancelButton.OnClickEvent -= onCancelClicked;
		}

		// ── 내부: 슬롯 풀 / 등록 칸 ────────────────────────────────────────

		private ItemSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			ItemSlot slot = Instantiate(_slotPrefab, _gridParent);
			slot.OnClicked += onGridSlotClicked;
			_slots.Add(slot);
			return slot;
		}

		private CurrencyDisplaySlot getOrCreateCostSlot(ModePanel panel, int index)
		{
			if (panel.costSlots == null)
			{
				panel.costSlots = new List<CurrencyDisplaySlot>();
			}

			if (index < panel.costSlots.Count)
			{
				return panel.costSlots[index];
			}

			CurrencyDisplaySlot slot = Instantiate(_costSlotPrefab, panel.costGrid);
			panel.costSlots.Add(slot);
			return slot;
		}

		private ModePanel getPanel(CraftMode mode)
		{
			switch (mode)
			{
			case CraftMode.Promote:
				return _promotePanel;

			case CraftMode.Transfer:
				return _transferPanel;
			}

			return _enhancePanel;
		}

		private RegisterSlotView findRegisterSlot(CraftSlotRoot type)
		{
			if (_registerSlots == null)
			{
				return null;
			}

			for (int i = 0; i < _registerSlots.Length; i++)
			{
				if (_registerSlots[i].type == type)
				{
					return _registerSlots[i];
				}
			}

			return null;
		}

		private void clearRegisterSlot(RegisterSlotView slotView)
		{
			if (slotView.instance != null)
			{
				slotView.instance.OnClicked -= onRegisteredSlotClicked;
				Destroy(slotView.instance.gameObject);
				slotView.instance = null;
			}
		}

		// 파괴된 Transform 을 트윈이 붙들지 않도록 화면이 사라질 때 전부 끊는다.
		private void killRegisterTweens()
		{
			if (_registerSlots == null)
			{
				return;
			}

			for (int i = 0; i < _registerSlots.Length; i++)
			{
				Transform root = _registerSlots[i].root;
				if (root != null)
				{
					root.DOKill();
				}
			}
		}
	}
}
