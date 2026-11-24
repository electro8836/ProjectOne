using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 장비(인벤토리) 화면의 UI Toolkit View(MVP). 레이아웃은 UIToolkit/Equipment/EquipmentScreen.uxml, 칸은 ItemCellView.
	// Presenter 는 UGUI 판과 같은 EquipmentPresenter 를 그대로 쓴다(IEquipmentView) — 정렬·탭·보관함·저장 규칙이 한 벌이다.
	// 이 View 는 칸을 그리고 입력을 이벤트로 넘기기만 한다.
	public class EquipmentToolkitUI : ToolkitScreen, IEquipmentView
	{
		private const int TAB_COUNT = 6;
		private const int TAB_CONSUMABLE = 5;

		// 장착 칸 — EquipSlotTypes 의 Weapon(1) ~ Relic(8). UXML 의 equip-1 ~ equip-8 과 같은 순서다.
		private const int EQUIP_SLOT_FIRST = 1;
		private const int EQUIP_SLOT_LAST = 8;

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO (UGUI 판과 같은 것)
		[SerializeField] private CharacterPreview _characterPreview;	// 같은 오브젝트 — 더미 히어로를 RenderTexture 로 찍는다
		[SerializeField] private EDT.Currency[] _headerCurrencies = { EDT.Currency.Gold, EDT.Currency.Dia };	// 제목 줄 재화 캡슐(최대 3)

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnTabSelected;
		public event Action<long, int> OnSlotClicked;
		public event Action OnStatClicked;
		public event Action OnCurrencyClicked;
		public event Action OnSortClicked;
		public event Action OnStashClicked;
		public event Action OnStashCloseClicked;
		public event Action OnDecompositionClicked;

		private readonly EquipmentPresenter _presenter = new EquipmentPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<UniTask> _iconTasks = new List<UniTask>();
		private HeaderCurrencyBar _currencyBar;

		private sealed class EquipSlotRefs
		{
			public EquipSlotTypes type;
			public VisualElement root;
			public VisualElement placeholder;	// 빈 칸일 때 보이는 흐린 분류 아이콘
			public ItemCellView cell;
		}

		private readonly Button[] _tabs = new Button[TAB_COUNT];
		private int _selectedTab = -1;

		private Label _nickname;
		private Label _level;
		private Label _battlePower;
		private Label _capacity;
		private Label _stashCapacity;
		private Button _sortButton;
		private VisualElement _stashSection;
		private Button _stashButton;
		private VisualElement _character;

		private ScrollView _itemGrid;
		private ScrollView _stashGrid;
		private readonly List<ItemCellView> _gridCells = new List<ItemCellView>();
		private readonly List<ItemCellView> _stashCells = new List<ItemCellView>();
		private readonly List<EquipSlotRefs> _equipSlots = new List<EquipSlotRefs>();

		protected override void OnBind(VisualElement root)
		{
			bindElements(root);
			_presenter.Initialize(this);

			if (_characterPreview != null)
			{
				_characterPreview.OnTextureReady += onCharacterTextureReady;
			}
		}

		// 전투력은 전용 이벤트가 없어 Presenter 가 스탯 버전을 폴링한다 (UGUI 판과 같은 구조).
		private void Update()
		{
			if (IsBound == false)
			{
				return;
			}

			_presenter.Tick();
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				if (_characterPreview != null)
				{
					_characterPreview.OnTextureReady -= onCharacterTextureReady;
				}

				_presenter.Dispose();
				_currencyBar.Dispose();
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
			root.Q<Button>("stat-button").clicked += onStatButtonClicked;
			root.Q<Button>("currency-button").clicked += onCurrencyButtonClicked;
			_stashButton = root.Q<Button>("stash-button");
			_stashButton.clicked += onStashButtonClicked;
			root.Q<Button>("stash-close-button").clicked += onStashCloseButtonClicked;
			root.Q<Button>("decomposition-button").clicked += onDecompositionButtonClicked;

			_currencyBar = new HeaderCurrencyBar(root.Q("header-currencies"), _headerCurrencies, _icons, this.GetCancellationTokenOnDestroy());

			_sortButton = root.Q<Button>("sort-button");
			_sortButton.clicked += onSortButtonClicked;

			for (int i = 0; i < TAB_COUNT; i++)
			{
				Button tab = root.Q<Button>("tab-" + i);
				tab.userData = i;
				tab.RegisterCallback<ClickEvent>(onTabElementClicked);
				_tabs[i] = tab;
			}

			_nickname = root.Q<Label>("nickname");
			_level = root.Q<Label>("hero-level");
			_battlePower = root.Q<Label>("battle-power");
			_capacity = root.Q<Label>("capacity");
			_stashCapacity = root.Q<Label>("stash-capacity");
			_stashSection = root.Q("stash-section");
			_character = root.Q("character");

			_itemGrid = root.Q<ScrollView>("item-grid");
			_itemGrid.AddManipulator(new ScrollDragManipulator(_itemGrid));
			_stashGrid = root.Q<ScrollView>("stash-grid");
			_stashGrid.AddManipulator(new ScrollDragManipulator(_stashGrid));

			for (int i = EQUIP_SLOT_FIRST; i <= EQUIP_SLOT_LAST; i++)
			{
				EquipSlotRefs slot = new EquipSlotRefs();
				slot.type = (EquipSlotTypes)i;
				slot.root = root.Q("equip-" + i);
				slot.placeholder = slot.root.Q(className: "equip-slot__placeholder");
				slot.cell = createCell(slot.root);
				clearEquipSlot(slot);
				_equipSlots.Add(slot);
			}
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// 탭만 선택(OnTabSelected 를 발행하지 않음 — 초기 표시용).
		// 탭이 바뀌면 목록을 맨 위로 되돌린다 — 긴 목록에서 내려 둔 위치가 짧은 목록에 남으면 칸이 화면 밖에 있어 빈 목록처럼 보인다.
		public void SelectTab(int index)
		{
			_selectedTab = index;
			for (int i = 0; i < TAB_COUNT; i++)
			{
				_tabs[i].EnableInClassList("tab--selected", i == index);
			}

			_itemGrid.scrollOffset = Vector2.zero;
			_stashGrid.scrollOffset = Vector2.zero;
		}

		public UniTask RenderGridAsync(IReadOnlyList<ItemSlotData> data, CancellationToken ct)
		{
			return renderGridAsync(_itemGrid, _gridCells, data, ct);
		}

		public UniTask RenderStashGridAsync(IReadOnlyList<ItemSlotData> data, CancellationToken ct)
		{
			return renderGridAsync(_stashGrid, _stashCells, data, ct);
		}

		public async UniTask RenderEquippedAsync(IReadOnlyList<EquippedSlotData> data, CancellationToken ct)
		{
			_iconTasks.Clear();
			for (int i = 0; i < _equipSlots.Count; i++)
			{
				EquipSlotRefs slot = _equipSlots[i];
				EquippedSlotData d = findEquipped(data, slot.type);
				if (d.instance == null)
				{
					clearEquipSlot(slot);
					continue;
				}

				// 이 칸에 있다는 것이 곧 장착중이라 장착 표시는 따로 켜지 않는다.
				setShown(slot.placeholder, false);
				slot.cell.SetShown(true);
				_iconTasks.Add(slot.cell.BindEquipmentAsync(d.instance, false, ct));
			}

			await UniTask.WhenAll(_iconTasks).SuppressCancellationThrow();
		}

		// "대검 레벨 12" — 장착 무기의 마스터리 레벨. 무기가 없으면 감춘다.
		public void RenderLevel(string weaponName, int masteryLevel)
		{
			_level.text = (masteryLevel > 0) ? weaponName + " 레벨 " + masteryLevel.ToString() : string.Empty;
			_level.style.display = (masteryLevel > 0) ? DisplayStyle.Flex : DisplayStyle.None;
		}

		public void RenderBattlePower(int power)
		{
			_battlePower.text = "전투력 " + power.ToString("N0");
		}

		public void SetNickname(string nickname)
		{
			_nickname.text = nickname;
		}

		// "12/40" — 초과분 색 태그(<color=red>)까지 Presenter 가 만들어 넘긴다. Label 은 리치 텍스트를 그대로 그린다.
		public void RenderInventoryCapacity(string text)
		{
			_capacity.text = text;
		}

		public void RenderStashCapacity(string text)
		{
			_stashCapacity.text = text;
		}

		// 보관함은 기능 버튼 줄·캐릭터·장착 칸 영역을 통째로 덮는다 — 아래 가방은 그대로라 장비를 오가며 옮길 수 있다.
		// 열려 있는 동안 보관함 버튼은 초록(btn--on)으로 켜짐을 보인다.
		public void SetStashVisible(bool visible)
		{
			_stashSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			_stashButton.EnableInClassList("btn--on", visible);
		}

		public void SetConsumableTabVisible(bool visible)
		{
			_tabs[TAB_CONSUMABLE].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		public void SetSortLabel(string label)
		{
			_sortButton.text = label;
		}

		public void SetSortVisible(bool visible)
		{
			_sortButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 장착/해제된 칸을 강조한다 — 크게 키운 상태(전환 없음)에서 다음 프레임에 클래스를 떼어 제자리로 줄어들게 한다.
		public void PlayEquippedSlotPop(EquipSlotTypes type)
		{
			for (int i = 0; i < _equipSlots.Count; i++)
			{
				if (_equipSlots[i].type != type)
				{
					continue;
				}

				VisualElement root = _equipSlots[i].root;
				root.AddToClassList("equip-slot--pop");
				root.schedule.Execute(clearAllPops);
				return;
			}
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onTabElementClicked(ClickEvent evt)
		{
			VisualElement tab = evt.currentTarget as VisualElement;
			int index = (int)tab.userData;
			if (index == _selectedTab)
			{
				return;
			}

			SelectTab(index);
			if (OnTabSelected != null) { OnTabSelected.Invoke(index); }
		}

		private void onCellClicked(ItemCellView cell, long uid, int itemId)
		{
			if (OnSlotClicked != null) { OnSlotClicked.Invoke(uid, itemId); }
		}

		private void onStatButtonClicked()
		{
			if (OnStatClicked != null) { OnStatClicked.Invoke(); }
		}

		private void onCurrencyButtonClicked()
		{
			if (OnCurrencyClicked != null) { OnCurrencyClicked.Invoke(); }
		}

		private void onSortButtonClicked()
		{
			if (OnSortClicked != null) { OnSortClicked.Invoke(); }
		}

		private void onStashButtonClicked()
		{
			if (OnStashClicked != null) { OnStashClicked.Invoke(); }
		}

		private void onStashCloseButtonClicked()
		{
			if (OnStashCloseClicked != null) { OnStashCloseClicked.Invoke(); }
		}

		private void onDecompositionButtonClicked()
		{
			if (OnDecompositionClicked != null) { OnDecompositionClicked.Invoke(); }
		}

		// 프리뷰 RenderTexture 가 준비되면 무대 배경으로 건다. 이후 착용 변경은 같은 텍스처에 다시 찍힌다.
		private void onCharacterTextureReady(RenderTexture texture)
		{
			_character.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));
		}

		private void clearAllPops()
		{
			for (int i = 0; i < _equipSlots.Count; i++)
			{
				_equipSlots[i].root.RemoveFromClassList("equip-slot--pop");
			}
		}

		// ── 내부: 칸 풀 / 바인딩 ──────────────────────────────────────────

		private async UniTask renderGridAsync(ScrollView grid, List<ItemCellView> cells, IReadOnlyList<ItemSlotData> data, CancellationToken ct)
		{
			_iconTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				ItemCellView cell = getOrCreateCell(grid, cells, i);
				cell.SetShown(true);

				ItemSlotData d = data[i];
				if (d.instance != null)
				{
					// 장착중 표시는 보관함 모드의 목록에서만 켜진다 (EquipmentPresenter 가 정한다).
					_iconTasks.Add(cell.BindEquipmentAsync(d.instance, d.equipped, ct));
				}
				else
				{
					_iconTasks.Add(cell.BindItemAsync(d.row, d.count, ct));
				}
			}

			for (int i = data.Count; i < cells.Count; i++)
			{
				cells[i].SetShown(false);
			}

			await UniTask.WhenAll(_iconTasks).SuppressCancellationThrow();
		}

		private ItemCellView getOrCreateCell(ScrollView grid, List<ItemCellView> cells, int index)
		{
			if (index < cells.Count)
			{
				return cells[index];
			}

			ItemCellView cell = createCell(grid);
			cells.Add(cell);
			return cell;
		}

		private ItemCellView createCell(VisualElement parent)
		{
			ItemCellView cell = new ItemCellView(parent, _gradeColors, _icons);
			cell.OnClicked += onCellClicked;
			return cell;
		}

		// 빈 장착 칸 — 칸을 감춰 슬롯 틀과 흐린 분류 아이콘만 보이게 한다. 클릭도 받지 않는다.
		private void clearEquipSlot(EquipSlotRefs slot)
		{
			slot.cell.Clear();
			slot.cell.SetShown(false);
			setShown(slot.placeholder, true);
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private static EquippedSlotData findEquipped(IReadOnlyList<EquippedSlotData> data, EquipSlotTypes type)
		{
			for (int i = 0; i < data.Count; i++)
			{
				if (data[i].type == type)
				{
					return data[i];
				}
			}

			return default;
		}
	}
}
