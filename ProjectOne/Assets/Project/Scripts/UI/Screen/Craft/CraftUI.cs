using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Items;
using ProjectOne.Upgrade;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 제작 화면의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/Screens/Craft/CraftScreen.uxml.
	// 네비게이션 바의 Craft 탭으로 열린다 (UIPrefab_CraftInfo).
	// 모드 탭으로 강화·승급·전이를 바꾸고, 아래 목록에서 고른 장비를 모드별 등록 칸에 올린다.
	// 표시와 입력 전달만 담당하고, 등록·실행 결정은 CraftPresenter 가 한다.
	//
	// 비용·불가 문구·버튼 줄은 세 모드가 함께 쓴다 — Presenter 가 모드별로 알려 준 상태를 기억해 두었다가
	// 지금 보이는 모드의 것만 그린다.
	public class CraftUI : ToolkitScreen, IView
	{
		private const int MODE_COUNT = 3;
		private const int FILTER_COUNT = 5;

		private static readonly string[] ActionLabels = { "강화", "승급", "전이" };

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnModeSelected;
		public event Action<int> OnFilterSelected;
		public event Action<long> OnGridSlotClicked;
		public event Action<CraftSlotRoot> OnRegisteredSlotClicked;
		public event Action OnActionClicked;
		public event Action OnActionHeld;	// 실행 버튼을 누른 채 반복 — 강화만 쓴다
		public event Action OnCancelClicked;
		public event Action OnHomeClicked;
		public event Action OnSortClicked;

		// 모드 하나의 비용 영역 상태 — 보이는 모드가 바뀔 때 다시 그린다.
		private sealed class ModeState
		{
			public bool visible;
			public string disableMessage;
			public readonly List<UpgradeCost> costs = new List<UpgradeCost>();
			public bool actionInteractable = true;
			public bool cancelVisible;
		}

		private sealed class RegisterSlot
		{
			public CraftSlotRoot type;
			public VisualElement root;
			public VisualElement hint;
			public ItemCellView cell;
		}

		private readonly CraftPresenter _presenter = new CraftPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private readonly Button[] _modeTabs = new Button[MODE_COUNT];
		private readonly VisualElement[] _panels = new VisualElement[MODE_COUNT];
		private readonly ModeState[] _modeStates = new ModeState[MODE_COUNT];
		private readonly Button[] _filterTabs = new Button[FILTER_COUNT];
		private readonly List<RegisterSlot> _registerSlots = new List<RegisterSlot>();
		private readonly List<ItemCellView> _gridCells = new List<ItemCellView>();
		private readonly List<CostView> _costViews = new List<CostView>();

		private CraftMode _shownMode = CraftMode.Enhance;
		private Label _requirementCaption;
		private VisualElement _costRow;
		private Label _disableText;
		private Button _actionButton;
		private Button _cancelButton;
		private HoldRepeatManipulator _hold;
		private Button _sortButton;
		private ScrollView _grid;

		protected override void OnBind(VisualElement root)
		{
			for (int i = 0; i < MODE_COUNT; i++)
			{
				_modeStates[i] = new ModeState();
				_panels[i] = root.Q("panel-" + i);

				Button tab = root.Q<Button>("mode-" + i);
				tab.userData = i;
				tab.RegisterCallback<ClickEvent>(onModeTabClicked);
				_modeTabs[i] = tab;
			}

			for (int i = 0; i < FILTER_COUNT; i++)
			{
				Button tab = root.Q<Button>("filter-" + i);
				tab.userData = i;
				tab.RegisterCallback<ClickEvent>(onFilterTabClicked);
				_filterTabs[i] = tab;
			}

			addRegisterSlot(root, "slot-enhance", CraftSlotRoot.Enhance);
			addRegisterSlot(root, "slot-promote-prev", CraftSlotRoot.PromotePrev);
			addRegisterSlot(root, "slot-promote-next", CraftSlotRoot.PromoteNext);
			addRegisterSlot(root, "slot-transfer-source", CraftSlotRoot.TransferSource);
			addRegisterSlot(root, "slot-transfer-target", CraftSlotRoot.TransferTarget);

			_requirementCaption = root.Q<Label>("requirement-caption");
			_costRow = root.Q("cost-row");
			_disableText = root.Q<Label>("disable-text");

			_actionButton = root.Q<Button>("action-button");
			_actionButton.clicked += onActionClicked;
			_hold = new HoldRepeatManipulator(onActionHeld);
			_actionButton.AddManipulator(_hold);

			_cancelButton = root.Q<Button>("cancel-button");
			_cancelButton.clicked += onCancelClicked;

			_sortButton = root.Q<Button>("sort-button");
			_sortButton.clicked += onSortClicked;
			root.Q<Button>("home-button").clicked += onHomeClicked;

			_grid = root.Q<ScrollView>("grid");
			_grid.AddManipulator(new ScrollDragManipulator(_grid));

			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_presenter.Dispose();
			}

			_icons.ReleaseAll();
			base.OnDestroy();
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

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// 탭만 선택(OnModeSelected 를 발행하지 않음 — 초기 표시용).
		public void SelectModeTab(int index)
		{
			selectTabs(_modeTabs, index);
		}

		public void SelectFilterTab(int index)
		{
			selectTabs(_filterTabs, index);
		}

		public void SetSortLabel(string label)
		{
			_sortButton.text = label;
		}

		// 해당 모드 패널만 켜고, 공용 비용·버튼 줄을 그 모드 상태로 다시 그린다.
		public void ShowMode(CraftMode mode)
		{
			_shownMode = mode;
			for (int i = 0; i < MODE_COUNT; i++)
			{
				_panels[i].EnableInClassList("craft-panel--shown", i == (int)mode);
			}

			_actionButton.text = ActionLabels[(int)mode];
			applyModeState(GetDestroyToken()).Forget();
		}

		public async UniTask RenderGridAsync(IReadOnlyList<CraftSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				ItemCellView cell = getOrCreateGridCell(i);
				cell.SetShown(true);
				_bindTasks.Add(cell.BindEquipmentAsync(data[i].instance, data[i].equipped, ct));
			}

			for (int i = data.Count; i < _gridCells.Count; i++)
			{
				_gridCells[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 등록 칸 렌더 — instance 가 null 이면 비운다(빈 틀과 안내 글자만 보인다).
		public UniTask RenderRegisteredAsync(CraftSlotRoot type, EquipmentInstance instance, CancellationToken ct)
		{
			RegisterSlot slot = findRegisterSlot(type);
			if (slot == null)
			{
				return UniTask.CompletedTask;
			}

			if (instance == null)
			{
				slot.cell.Clear();
				slot.cell.SetShown(false);
				setShown(slot.hint, true);
				return UniTask.CompletedTask;
			}

			setShown(slot.hint, false);
			slot.cell.SetShown(true);

			// 등록 칸에서는 장착 여부를 보여주지 않는다 — 목록에서 이미 확인하고 올린 것이다.
			return slot.cell.BindEquipmentAsync(instance, false, ct);
		}

		// 비용 영역 — visible 이 false 면(등록된 것이 없음) 모두 숨긴다. disableMessage 가 있으면 불가 문구만 보인다.
		public UniTask RenderRequirementAsync(CraftMode mode, bool visible, IReadOnlyList<UpgradeCost> costs, string disableMessage, CancellationToken ct)
		{
			ModeState state = _modeStates[(int)mode];
			state.visible = visible;
			state.disableMessage = disableMessage;
			state.costs.Clear();
			if (costs != null)
			{
				for (int i = 0; i < costs.Count; i++)
				{
					state.costs.Add(costs[i]);
				}
			}

			if (mode != _shownMode)
			{
				return UniTask.CompletedTask;
			}

			return renderRequirementAsync(state, ct);
		}

		public void SetActionInteractable(CraftMode mode, bool interactable)
		{
			_modeStates[(int)mode].actionInteractable = interactable;
			if (mode == _shownMode)
			{
				_actionButton.SetEnabled(interactable);
			}
		}

		public void SetCancelVisible(CraftMode mode, bool visible)
		{
			_modeStates[(int)mode].cancelVisible = visible;
			if (mode == _shownMode)
			{
				setShown(_cancelButton, visible);
			}
		}

		// 실행 성공 강조 — 크게 키운 상태(전환 없음)에서 다음 프레임에 클래스를 떼어 제자리로 줄어들게 한다.
		public void PlayPop(CraftSlotRoot type)
		{
			RegisterSlot slot = findRegisterSlot(type);
			if (slot == null)
			{
				return;
			}

			slot.root.AddToClassList("craft-slot--pop");
			slot.root.schedule.Execute(clearPops);
		}

		// ── 내부: 표시 ────────────────────────────────────────────────────

		private UniTask applyModeState(CancellationToken ct)
		{
			ModeState state = _modeStates[(int)_shownMode];
			_actionButton.SetEnabled(state.actionInteractable);
			setShown(_cancelButton, state.cancelVisible);
			return renderRequirementAsync(state, ct);
		}

		private async UniTask renderRequirementAsync(ModeState state, CancellationToken ct)
		{
			bool disabled = state.visible == true && string.IsNullOrEmpty(state.disableMessage) == false;
			bool showCosts = state.visible == true && disabled == false;

			setShown(_disableText, disabled);
			_disableText.text = disabled ? state.disableMessage : string.Empty;
			setShown(_requirementCaption, showCosts);
			setShown(_costRow, showCosts);
			if (showCosts == false)
			{
				return;
			}

			_bindTasks.Clear();
			for (int i = 0; i < state.costs.Count; i++)
			{
				CostView view = getOrCreateCost(i);
				view.SetShown(true);
				_bindTasks.Add(view.BindAsync(state.costs[i], ct));
			}

			for (int i = state.costs.Count; i < _costViews.Count; i++)
			{
				_costViews[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		private void clearPops()
		{
			for (int i = 0; i < _registerSlots.Count; i++)
			{
				_registerSlots[i].root.RemoveFromClassList("craft-slot--pop");
			}
		}

		private static void selectTabs(Button[] tabs, int index)
		{
			for (int i = 0; i < tabs.Length; i++)
			{
				tabs[i].EnableInClassList("tab--selected", i == index);
			}
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// ── 내부: 풀 / 등록 칸 ────────────────────────────────────────────

		private void addRegisterSlot(VisualElement root, string name, CraftSlotRoot type)
		{
			RegisterSlot slot = new RegisterSlot();
			slot.type = type;
			slot.root = root.Q(name);
			slot.hint = slot.root.Q(className: "craft-slot__hint");
			slot.cell = new ItemCellView(slot.root, _gradeColors, _icons);
			slot.cell.Tag = type;
			slot.cell.OnClicked += onRegisteredCellClicked;
			slot.cell.SetShown(false);
			_registerSlots.Add(slot);
		}

		private RegisterSlot findRegisterSlot(CraftSlotRoot type)
		{
			for (int i = 0; i < _registerSlots.Count; i++)
			{
				if (_registerSlots[i].type == type)
				{
					return _registerSlots[i];
				}
			}

			return null;
		}

		private ItemCellView getOrCreateGridCell(int index)
		{
			if (index < _gridCells.Count)
			{
				return _gridCells[index];
			}

			ItemCellView cell = new ItemCellView(_grid.contentContainer, _gradeColors, _icons);
			cell.OnClicked += onGridCellClicked;
			_gridCells.Add(cell);
			return cell;
		}

		private CostView getOrCreateCost(int index)
		{
			if (index < _costViews.Count)
			{
				return _costViews[index];
			}

			CostView view = new CostView(_costRow, _icons);
			_costViews.Add(view);
			return view;
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onModeTabClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			SelectModeTab(index);
			if (OnModeSelected != null) { OnModeSelected.Invoke(index); }
		}

		private void onFilterTabClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			SelectFilterTab(index);
			if (OnFilterSelected != null) { OnFilterSelected.Invoke(index); }
		}

		private void onGridCellClicked(ItemCellView sender, long uid, int itemId)
		{
			if (OnGridSlotClicked != null) { OnGridSlotClicked.Invoke(uid); }
		}

		// 등록 칸 클릭 — 어느 칸인지(type)만 올린다. 칸에 무엇이 있는지는 Presenter 가 안다.
		private void onRegisteredCellClicked(ItemCellView sender, long uid, int itemId)
		{
			if (OnRegisteredSlotClicked != null) { OnRegisteredSlotClicked.Invoke((CraftSlotRoot)sender.Tag); }
		}

		private void onActionClicked()
		{
			// 누른 채 반복이 나간 누름이면 뗄 때의 클릭은 건너뛴다.
			if (_hold.HoldFired == true)
			{
				return;
			}

			if (OnActionClicked != null) { OnActionClicked.Invoke(); }
		}

		// 누른 채 반복은 강화만 쓴다(UGUI 판 Enhance 버튼의 _holdRepeat 와 같다).
		private void onActionHeld()
		{
			if (_shownMode != CraftMode.Enhance)
			{
				return;
			}

			if (OnActionHeld != null) { OnActionHeld.Invoke(); }
		}

		private void onCancelClicked()
		{
			if (OnCancelClicked != null) { OnCancelClicked.Invoke(); }
		}

		private void onHomeClicked()
		{
			if (OnHomeClicked != null) { OnHomeClicked.Invoke(); }
		}

		private void onSortClicked()
		{
			if (OnSortClicked != null) { OnSortClicked.Invoke(); }
		}
	}
}
