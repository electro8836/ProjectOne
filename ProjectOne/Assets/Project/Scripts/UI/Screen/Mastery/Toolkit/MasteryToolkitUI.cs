using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 스킬 칸 1개 렌더 데이터.
	public struct MasterySlotViewData
	{
		public string iconAddress;
		public bool isEmpty;
		public bool isSelected;	// 선택한 스킬이 이 칸에 들어 있다
		public bool isTarget;	// 선택한 스킬을 이 칸에 넣을 수 있다
	}

	// 스킬 목록 1행 렌더 데이터.
	public struct MasterySkillRowData
	{
		public string iconAddress;
		public string name;
		public string categoryLabel;
		public string categoryClass;	// 칩 색 클래스 (chip--passive 등, 전술은 빈 문자열)
		public string stateText;
		public bool isUnlocked;
		public bool isEquipped;
		public bool isSelected;
	}

	// 스킬 상세 팝업 렌더 데이터.
	public struct MasterySkillDetailData
	{
		public bool isOpen;
		public string iconAddress;
		public string name;
		public string stateText;	// "장착 중 · 전술 2" / "Lv.70 해금 필요" 등
		public string categoryLabel;
		public string categoryClass;
		public string description;
		public string hint;
		public bool showEquip;
		public bool showUnequip;
		public bool canInteract;
	}

	// 마스터리 화면의 UI Toolkit View(MVP). 레이아웃은 UIToolkit/Mastery/MasteryScreen.uxml, 스타일은 같은 폴더의 USS.
	// 표시와 입력 전달만 하고, 무엇을 보여줄지·장착 판정은 MasteryToolkitPresenter 가 정한다.
	// 목록은 끌어서 스크롤한다(터치는 ScrollView 기본, 마우스는 ScrollDragManipulator).
	public class MasteryToolkitUI : ToolkitScreen, IView
	{
		private const int TAB_CURRENT = 0;
		private const int TAB_ALL = 1;

		// 레벨 표기 — 숫자만 크게 금색(USS .level-text), "Lv."·"/ 최대" 는 작은 기본색
		private const string SUB_OPEN = "<size=36px><color=#F4E7C8>";
		private const string SUB_CLOSE = "</color></size>";

		[SerializeField] private VisualTreeAsset _skillRowTemplate;		// MasterySkillRow.uxml
		[SerializeField] private VisualTreeAsset _masteryRowTemplate;	// MasteryListRow.uxml

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnTabSelected;
		public event Action OnCloseClicked;
		public event Action<int> OnSkillClicked;	// 스킬 목록 행 인덱스
		public event Action<int> OnSlotClicked;		// 칸 인덱스 0~5
		public event Action OnEquipClicked;
		public event Action OnUnequipClicked;
		public event Action OnPopupCloseClicked;	// X 버튼 또는 바깥 어두운 곳
		public event Action OnSlotHintCancelClicked;

		private readonly MasteryToolkitPresenter _presenter = new MasteryToolkitPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<UniTask> _iconTasks = new List<UniTask>();

		// 행 요소 참조 — 렌더마다 Q 로 다시 찾지 않도록 처음 찍을 때 잡아 둔다.
		private sealed class SkillRowRefs
		{
			public VisualElement root;
			public VisualElement icon;
			public Label name;
			public Label category;
			public Label state;
		}

		private sealed class MasteryRowRefs
		{
			public VisualElement root;
			public VisualElement icon;
			public Label name;
			public Label level;
			public VisualElement fill;
			public Label gaugeText;
			public Label bonus;
		}

		private sealed class SlotRefs
		{
			public VisualElement root;
			public VisualElement icon;
		}

		private Button _tabCurrent;
		private Button _tabAll;
		private Button _closeButton;
		private VisualElement _pageCurrent;
		private VisualElement _pageAll;

		private VisualElement _infoIcon;
		private Label _infoName;
		private Label _infoLevel;
		private VisualElement _infoExpFill;
		private Label _infoExpText;

		private readonly SlotRefs[] _slots = new SlotRefs[6];

		private ScrollView _skillList;
		private readonly List<SkillRowRefs> _skillRows = new List<SkillRowRefs>();

		private VisualElement _slotHint;

		private VisualElement _popup;
		private bool _isPopupShown;
		private VisualElement _popupIcon;
		private Label _popupName;
		private Label _popupCategory;
		private Label _popupState;
		private Label _popupDesc;
		private Label _popupHint;
		private Button _equipButton;
		private Button _unequipButton;

		private Label _totalLevel;
		private VisualElement _totalFill;
		private Label _totalText;
		private Label _totalBonus;
		private ScrollView _masteryList;
		private readonly List<MasteryRowRefs> _masteryRows = new List<MasteryRowRefs>();

		protected override void OnBind(VisualElement root)
		{
			bindElements(root);
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

		// ── 요소 연결 ──────────────────────────────────────────────────────

		private void bindElements(VisualElement root)
		{
			_tabCurrent = root.Q<Button>("tab-current");
			_tabAll = root.Q<Button>("tab-all");
			_closeButton = root.Q<Button>("close-button");
			_pageCurrent = root.Q("page-current");
			_pageAll = root.Q("page-all");

			_tabCurrent.clicked += onTabCurrentClicked;
			_tabAll.clicked += onTabAllClicked;
			_closeButton.clicked += onCloseButtonClicked;

			_infoIcon = root.Q("info-icon");
			_infoName = root.Q<Label>("info-name");
			_infoLevel = root.Q<Label>("info-level");
			_infoExpFill = root.Q("info-exp-fill");
			_infoExpText = root.Q<Label>("info-exp-text");

			for (int i = 0; i < _slots.Length; i++)
			{
				SlotRefs slot = new SlotRefs();
				slot.root = root.Q("slot-" + i);
				slot.icon = slot.root.Q(className: "slot__icon");
				slot.root.userData = i;
				slot.root.RegisterCallback<ClickEvent>(onSlotElementClicked);
				slot.root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
				_slots[i] = slot;
			}

			_skillList = root.Q<ScrollView>("skill-list");
			_skillList.AddManipulator(new ScrollDragManipulator(_skillList));

			_slotHint = root.Q("slot-hint");
			root.Q<Button>("slot-hint-cancel").clicked += onSlotHintCancelButtonClicked;

			_popup = root.Q("popup");
			_popupIcon = root.Q("popup-icon");
			_popupName = root.Q<Label>("popup-name");
			_popupCategory = root.Q<Label>("popup-category");
			_popupState = root.Q<Label>("popup-state");
			_popupDesc = root.Q<Label>("popup-desc");
			_popupHint = root.Q<Label>("popup-hint");
			_equipButton = root.Q<Button>("equip-button");
			_unequipButton = root.Q<Button>("unequip-button");

			_equipButton.clicked += onEquipButtonClicked;
			_unequipButton.clicked += onUnequipButtonClicked;
			root.Q<Button>("popup-close").clicked += onPopupCloseButtonClicked;
			root.Q("popup-dim").RegisterCallback<ClickEvent>(onPopupDimClicked);

			_totalLevel = root.Q<Label>("total-level");
			_totalFill = root.Q("total-fill");
			_totalText = root.Q<Label>("total-text");
			_totalBonus = root.Q<Label>("total-bonus");
			_masteryList = root.Q<ScrollView>("mastery-list");
			_masteryList.AddManipulator(new ScrollDragManipulator(_masteryList));
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// 무기 미착용이면 현재 무기 탭을 잠근다.
		public void SetCurrentTabLocked(bool locked)
		{
			_tabCurrent.SetEnabled(locked == false);
		}

		// 탭 선택 표시 + 페이지 전환. 두 페이지는 배타다.
		public void ShowTab(int index)
		{
			bool current = index == TAB_CURRENT;

			_tabCurrent.EnableInClassList("tab--selected", current);
			_tabAll.EnableInClassList("tab--selected", current == false);

			_pageCurrent.style.display = current ? DisplayStyle.Flex : DisplayStyle.None;
			_pageAll.style.display = current ? DisplayStyle.None : DisplayStyle.Flex;
		}

		// 현재 무기 페이지 — 정보·칸·목록. 아이콘만 비동기라 나머지는 먼저 채운다.
		public async UniTask RenderCurrentAsync(MasteryInfoData info, IReadOnlyList<MasterySlotViewData> slots,
			IReadOnlyList<MasterySkillRowData> skills, CancellationToken ct)
		{
			_iconTasks.Clear();

			_infoName.text = info.name;
			_infoLevel.text = SUB_OPEN + "Lv." + SUB_CLOSE + info.level.ToString();
			_infoExpFill.style.width = Length.Percent(info.expRatio * 100f);
			_infoExpText.text = info.isMaxLevel ? "MAX" : $"{info.expRatio * 100f:F2}%";
			_iconTasks.Add(_icons.SetAsync(_infoIcon, info.iconAddress, ct));

			for (int i = 0; i < _slots.Length && i < slots.Count; i++)
			{
				SlotRefs slot = _slots[i];
				MasterySlotViewData data = slots[i];

				slot.root.EnableInClassList("slot--empty", data.isEmpty);
				slot.root.EnableInClassList("slot--selected", data.isSelected);
				slot.root.EnableInClassList("slot--target", data.isTarget);
				_iconTasks.Add(_icons.SetAsync(slot.icon, data.iconAddress, ct));
			}

			for (int i = 0; i < skills.Count; i++)
			{
				SkillRowRefs row = getOrCreateSkillRow(i);
				MasterySkillRowData data = skills[i];

				row.root.style.display = DisplayStyle.Flex;
				row.name.text = data.name;
				row.category.text = data.categoryLabel;
				row.category.EnableInClassList("chip--passive", data.categoryClass == "chip--passive");
				row.category.EnableInClassList("chip--core", data.categoryClass == "chip--core");
				row.state.text = data.stateText;

				row.root.EnableInClassList("skill-row--locked", data.isUnlocked == false);
				row.root.EnableInClassList("skill-row--equipped", data.isEquipped);
				row.root.EnableInClassList("skill-row--selected", data.isSelected);

				_iconTasks.Add(_icons.SetAsync(row.icon, data.iconAddress, ct));
			}

			for (int i = skills.Count; i < _skillRows.Count; i++)
			{
				_skillRows[i].root.style.display = DisplayStyle.None;
			}

			await UniTask.WhenAll(_iconTasks);
		}

		// 칸 고르기 안내(바꿀 칸을 누르세요 + 취소).
		public void SetSlotHintVisible(bool visible)
		{
			_slotHint.EnableInClassList("slot-hint--visible", visible);
		}

		// 스킬 상세 팝업. 닫혀 있다가 열릴 때만 등장 애니메이션을 건다.
		public void RenderPopup(MasterySkillDetailData data)
		{
			if (data.isOpen == false)
			{
				_isPopupShown = false;
				_popup.RemoveFromClassList("popup--open");
				_popup.style.display = DisplayStyle.None;
				return;
			}

			_popupName.text = data.name;
			_popupCategory.text = data.categoryLabel;
			_popupCategory.EnableInClassList("chip--passive", data.categoryClass == "chip--passive");
			_popupCategory.EnableInClassList("chip--core", data.categoryClass == "chip--core");
			_popupState.text = data.stateText;
			_popupDesc.text = data.description;
			_popupHint.text = data.hint;
			_popupHint.style.display = string.IsNullOrEmpty(data.hint) ? DisplayStyle.None : DisplayStyle.Flex;

			_equipButton.style.display = data.showEquip ? DisplayStyle.Flex : DisplayStyle.None;
			_unequipButton.style.display = data.showUnequip ? DisplayStyle.Flex : DisplayStyle.None;
			_equipButton.SetEnabled(data.canInteract);
			_unequipButton.SetEnabled(data.canInteract);

			_icons.SetAsync(_popupIcon, data.iconAddress, GetDestroyToken()).Forget();

			if (_isPopupShown == false)
			{
				_isPopupShown = true;
				// display 를 켠 같은 프레임에 클래스를 붙이면 전환이 생략된다 — 한 프레임 뒤에 붙인다.
				_popup.style.display = DisplayStyle.Flex;
				_popup.schedule.Execute(openPopupAnimated);
			}
		}

		private void openPopupAnimated()
		{
			// 그 사이에 닫혔으면 붙이지 않는다
			if (_isPopupShown == true)
			{
				_popup.AddToClassList("popup--open");
			}
		}

		// 전체 마스터리 페이지.
		public async UniTask RenderAllAsync(MasteryTotalData total, IReadOnlyList<MasteryInfoSlotData> list, CancellationToken ct)
		{
			_iconTasks.Clear();

			_totalLevel.text = total.totalLevel.ToString() + SUB_OPEN + " / " + total.maxTotalLevel.ToString() + SUB_CLOSE;
			_totalFill.style.width = Length.Percent(total.ratio * 100f);
			_totalText.text = total.isMax ? "MAX" : $"{total.ratio * 100f:F2}%";
			_totalBonus.text = total.bonusText;

			for (int i = 0; i < list.Count; i++)
			{
				MasteryRowRefs row = getOrCreateMasteryRow(i);
				MasteryInfoSlotData data = list[i];
				float ratio = data.maxLevel > 0 ? (float)data.level / data.maxLevel : 0f;

				row.root.style.display = DisplayStyle.Flex;
				row.root.EnableInClassList("mastery-row--active", data.isActive);
				row.name.text = data.name;
				row.level.text = SUB_OPEN + "Lv." + SUB_CLOSE + data.level.ToString();
				row.fill.style.width = Length.Percent(ratio * 100f);
				row.gaugeText.text = $"{data.level}/{data.maxLevel}";
				row.bonus.text = data.bonusText;

				_iconTasks.Add(_icons.SetAsync(row.icon, data.iconAddress, ct));
			}

			for (int i = list.Count; i < _masteryRows.Count; i++)
			{
				_masteryRows[i].root.style.display = DisplayStyle.None;
			}

			await UniTask.WhenAll(_iconTasks);
		}

		// ── 내부: 행 생성 (풀 재사용) ──────────────────────────────────────

		private SkillRowRefs getOrCreateSkillRow(int index)
		{
			if (index < _skillRows.Count)
			{
				return _skillRows[index];
			}

			// Instantiate 는 TemplateContainer 로 감싸 준다 — 껍데기 없이 행 요소만 목록에 붙인다.
			VisualElement root = _skillRowTemplate.Instantiate().Q(className: "skill-row");
			root.userData = index;
			root.RegisterCallback<ClickEvent>(onSkillRowClicked);
			root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
			_skillList.Add(root);

			SkillRowRefs row = new SkillRowRefs();
			row.root = root;
			row.icon = root.Q("icon");
			row.name = root.Q<Label>("name");
			row.category = root.Q<Label>("category");
			row.state = root.Q<Label>("state");
			_skillRows.Add(row);
			return row;
		}

		private MasteryRowRefs getOrCreateMasteryRow(int index)
		{
			if (index < _masteryRows.Count)
			{
				return _masteryRows[index];
			}

			VisualElement root = _masteryRowTemplate.Instantiate().Q(className: "mastery-row");
			_masteryList.Add(root);

			MasteryRowRefs row = new MasteryRowRefs();
			row.root = root;
			row.icon = root.Q("icon");
			row.name = root.Q<Label>("name");
			row.level = root.Q<Label>("level");
			row.fill = root.Q("fill");
			row.gaugeText = root.Q<Label>("gauge-text");
			row.bonus = root.Q<Label>("bonus");
			_masteryRows.Add(row);
			return row;
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onTabCurrentClicked()
		{
			if (OnTabSelected != null) { OnTabSelected.Invoke(TAB_CURRENT); }
		}

		private void onTabAllClicked()
		{
			if (OnTabSelected != null) { OnTabSelected.Invoke(TAB_ALL); }
		}

		private void onCloseButtonClicked()
		{
			if (OnCloseClicked != null) { OnCloseClicked.Invoke(); }
		}

		private void onSlotElementClicked(ClickEvent evt)
		{
			VisualElement target = evt.currentTarget as VisualElement;
			if (target == null || OnSlotClicked == null)
			{
				return;
			}

			OnSlotClicked.Invoke((int)target.userData);
		}

		private void onSkillRowClicked(ClickEvent evt)
		{
			VisualElement target = evt.currentTarget as VisualElement;
			if (target == null || OnSkillClicked == null)
			{
				return;
			}

			OnSkillClicked.Invoke((int)target.userData);
		}

		private void onEquipButtonClicked()
		{
			if (OnEquipClicked != null) { OnEquipClicked.Invoke(); }
		}

		private void onUnequipButtonClicked()
		{
			if (OnUnequipClicked != null) { OnUnequipClicked.Invoke(); }
		}

		private void onPopupCloseButtonClicked()
		{
			if (OnPopupCloseClicked != null) { OnPopupCloseClicked.Invoke(); }
		}

		private void onPopupDimClicked(ClickEvent evt)
		{
			if (OnPopupCloseClicked != null) { OnPopupCloseClicked.Invoke(); }
		}

		private void onSlotHintCancelButtonClicked()
		{
			if (OnSlotHintCancelClicked != null) { OnSlotHintCancelClicked.Invoke(); }
		}
	}
}
