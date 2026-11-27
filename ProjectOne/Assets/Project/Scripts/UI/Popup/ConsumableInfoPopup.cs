using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;

namespace ProjectOne.UI
{
	// 소모품 정보 팝업(UIPrefab_ConsumablePopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/ItemInfo/ConsumablePopup.uxml.
	// 등급색 제목 줄(이름·등급 박스)과 아이템 칸·보유 수·사용 효과, 아래 조작 띠(수량 +/-·사용), 오른쪽 위 삭제를 둔다.
	// 읽기 전용이면 조작 띠와 삭제를 감춘다. 재료는 제작 화면에서 열면 설명을 칸 옆으로 옮기고 획득처 | 사용처 탭을 보인다.
	// 획득처의 '조합하기'나 사용처 줄을 눌러 닫으면 그 제작법을 돌려준다.
	// 무엇을 할지는 ConsumableInfoPresenter 가 정한다.
	public class ConsumableInfoPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		private const string CHIP_PREFIX = "material-source__chip--";
		private const string TAB_ON = "material-tab--on";
		private const int TAB_SOURCES = 0;
		private const int TAB_USES = 1;

		// 획득처 한 줄.
		private sealed class SourceRowView
		{
			public VisualElement root;
			public Label chip;
			public Label text;
			public Button combine;
		}

		// 사용처 한 줄 — 줄 전체가 버튼이다.
		private sealed class UseRowView
		{
			public VisualElement root;
			public ItemCellView cell;
			public Label name;
			public Label count;
		}

		public event Action OnPlusClicked;
		public event Action OnMinusClicked;
		public event Action OnUseClicked;
		public event Action OnDeleteClicked;
		public event Action OnExitClicked;

		private readonly ConsumableInfoPresenter _presenter = new ConsumableInfoPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<SourceRowView> _sourceRows = new List<SourceRowView>();
		private readonly List<UseRowView> _useRows = new List<UseRowView>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private VisualElement _head;
		private Label _name;
		private Label _grade;
		private Label _owned;
		private Label _sideDesc;
		private VisualElement _descBox;
		private Label _descTitle;
		private Label _desc;
		private VisualElement _sources;
		private VisualElement _sourceList;
		private VisualElement _useList;
		private Label _materialEmpty;
		private ScrollView _materialScroll;
		private Button _tabSources;
		private Button _tabUses;
		private Label _count;
		private VisualElement _control;
		private Button _plus;
		private Button _minus;
		private Button _use;
		private Button _delete;
		private ItemCellView _cell;

		private bool _readOnly;
		private int _sourceCount;
		private int _useCount;
		private int _resultRecipeId;
		private UniTaskCompletionSource<bool> _tcs;

		protected override void OnBind(VisualElement root)
		{
			_head = root.Q("head");
			_name = root.Q<Label>("name");
			_grade = root.Q<Label>("grade");
			_owned = root.Q<Label>("owned");
			_sideDesc = root.Q<Label>("side-desc");
			_descBox = root.Q("desc-box");
			_descTitle = root.Q<Label>("desc-title");
			_desc = root.Q<Label>("desc");
			_sources = root.Q("sources");
			_sourceList = root.Q("source-list");
			_useList = root.Q("use-list");
			_materialEmpty = root.Q<Label>("material-empty");
			_materialScroll = root.Q<ScrollView>("material-scroll");
			_materialScroll.AddManipulator(new ScrollDragManipulator(_materialScroll));
			_tabSources = root.Q<Button>("tab-sources");
			_tabSources.clicked += onTabSourcesClicked;
			_tabUses = root.Q<Button>("tab-uses");
			_tabUses.clicked += onTabUsesClicked;
			_count = root.Q<Label>("count");
			_control = root.Q("control");
			_cell = new ItemCellView(root.Q("cell"), _gradeColors, _icons);

			_plus = root.Q<Button>("plus");
			_plus.clicked += onPlusClicked;
			_minus = root.Q<Button>("minus");
			_minus.clicked += onMinusClicked;
			_use = root.Q<Button>("use");
			_use.clicked += onUseClicked;
			_delete = root.Q<Button>("delete");
			_delete.clicked += onDeleteClicked;

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

			_icons.ReleaseAll();
			base.OnDestroy();
		}

		// 닫힐 때까지 기다린다. showSources 면 재료의 획득처·사용처 탭을 보인다.
		// '조합하기'나 사용처 줄로 닫으면 그 제작법 ID, 그냥 닫으면 0.
		public async UniTask<int> ShowAsync(int itemId, bool readOnly, bool showSources, CancellationToken ct)
		{
			_resultRecipeId = 0;
			await _presenter.ShowAsync(itemId, readOnly, showSources, ct);
			return _resultRecipeId;
		}

		public void SetReadOnly(bool readOnly)
		{
			_readOnly = readOnly;
			bool usable = (readOnly == false);
			_control.style.display = usable ? DisplayStyle.Flex : DisplayStyle.None;
			_delete.style.display = usable ? DisplayStyle.Flex : DisplayStyle.None;
			applyControlsInteractable(usable, usable, usable, usable);
		}

		public void Reveal()
		{
			PlayPopupOpen();
		}

		public void SetInfo(Table_Item.Row row)
		{
			_name.text = row.Name;
			_grade.text = ItemGradeNames.Get(row.Grade);
			if (_gradeColors != null)
			{
				// 장비 정보 팝업과 같이 — 제목 판·아래 선·등급 박스를 등급색으로
				ItemGradeColorTable.GradeColor colors = _gradeColors.Get(row.Grade);
				_head.style.backgroundColor = colors.bg;
				_head.style.borderBottomColor = colors.border;
				_grade.style.backgroundColor = colors.border;
			}

			_desc.text = row.Desc;
			_sideDesc.text = row.Desc;
		}

		// 설명 판 제목 — 쓸 수 있는 소모품은 "사용 효과", 그 밖은 "설명".
		public void SetDescTitle(string title)
		{
			_descTitle.text = title;
		}

		// 재료 정보 — 획득처·사용처가 하나라도 있으면 설명을 칸 옆으로 옮기고 탭을 보인다(처음은 획득처 탭).
		// 둘 다 없으면 보유 수 · 설명 판의 원래 모양 그대로다. 사용처 칸 아이콘을 다 받은 뒤 끝난다.
		public async UniTask SetMaterialInfoAsync(IReadOnlyList<MaterialSourceData> sources, IReadOnlyList<MaterialUseData> uses, CancellationToken ct)
		{
			_sourceCount = sources.Count;
			_useCount = uses.Count;
			bool material = (_sourceCount > 0 || _useCount > 0);
			_sources.style.display = material ? DisplayStyle.Flex : DisplayStyle.None;
			_descBox.style.display = material ? DisplayStyle.None : DisplayStyle.Flex;
			_owned.style.display = material ? DisplayStyle.None : DisplayStyle.Flex;
			_sideDesc.style.display = material ? DisplayStyle.Flex : DisplayStyle.None;

			applySources(sources);
			_bindTasks.Clear();
			applyUses(uses, ct);
			selectTab(TAB_SOURCES);
			await UniTask.WhenAll(_bindTasks);
		}

		public UniTask BindItemSlotAsync(Table_Item.Row row, int ownedCount, CancellationToken ct)
		{
			_owned.text = "보유 " + ownedCount.ToString() + "개";
			return _cell.BindItemAsync(row, ownedCount, ct);
		}

		public void SetCount(int count)
		{
			_count.text = count.ToString();
		}

		public void SetControlsInteractable(bool plus, bool minus, bool use, bool delete)
		{
			applyControlsInteractable(plus, minus, use, delete);
		}

		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void CloseFromInput()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		private void applySources(IReadOnlyList<MaterialSourceData> sources)
		{
			for (int i = 0; i < sources.Count; i++)
			{
				SourceRowView row = getOrCreateSourceRow(i);
				MaterialSourceData source = sources[i];
				// 앞 줄과 같은 처치면 칩 자리만 지킨다 — 보스 이름이 한 열로 늘어선다
				bool cont = (i > 0 && source.kind == MaterialSourceKind.Boss && sources[i - 1].kind == MaterialSourceKind.Boss);
				row.root.style.display = DisplayStyle.Flex;
				row.root.EnableInClassList("material-source--first", i == 0);
				row.root.EnableInClassList("material-source--cont", cont);
				applyChip(row.chip, source.kind);
				row.text.text = source.text;
				row.combine.userData = source.recipeId;
				row.combine.style.display = (source.kind == MaterialSourceKind.Combine) ? DisplayStyle.Flex : DisplayStyle.None;
			}

			for (int i = sources.Count; i < _sourceRows.Count; i++)
			{
				_sourceRows[i].root.style.display = DisplayStyle.None;
			}
		}

		private void applyUses(IReadOnlyList<MaterialUseData> uses, CancellationToken ct)
		{
			for (int i = 0; i < uses.Count; i++)
			{
				UseRowView row = getOrCreateUseRow(i);
				MaterialUseData use = uses[i];
				Table_Item.Row item = Table_Item.Get(use.resultItemId);
				row.root.style.display = DisplayStyle.Flex;
				row.root.userData = use.recipeId;
				row.name.text = (item != null) ? item.Name : use.resultItemId.ToString();
				row.count.text = "×" + use.count;
				_bindTasks.Add(row.cell.BindItemAsync(item, 1, ct));
			}

			for (int i = uses.Count; i < _useRows.Count; i++)
			{
				_useRows[i].root.style.display = DisplayStyle.None;
			}
		}

		// 탭 전환 — 고른 쪽 목록만 보이고 맨 위로 올린다. 비었으면 안내 문구.
		private void selectTab(int tab)
		{
			bool sources = (tab == TAB_SOURCES);
			_tabSources.EnableInClassList(TAB_ON, sources);
			_tabUses.EnableInClassList(TAB_ON, sources == false);
			_sourceList.style.display = sources ? DisplayStyle.Flex : DisplayStyle.None;
			_useList.style.display = sources ? DisplayStyle.None : DisplayStyle.Flex;

			int count = sources ? _sourceCount : _useCount;
			_materialEmpty.style.display = (count == 0) ? DisplayStyle.Flex : DisplayStyle.None;
			_materialEmpty.text = sources ? "획득처가 없습니다" : "사용처가 없습니다";
			_materialScroll.scrollOffset = Vector2.zero;
		}

		private static void applyChip(Label chip, MaterialSourceKind kind)
		{
			chip.EnableInClassList(CHIP_PREFIX + "boss", kind == MaterialSourceKind.Boss);
			chip.EnableInClassList(CHIP_PREFIX + "combine", kind == MaterialSourceKind.Combine);
			chip.EnableInClassList(CHIP_PREFIX + "decompose", kind == MaterialSourceKind.Decompose);
			switch (kind)
			{
				case MaterialSourceKind.Boss:
					chip.text = "처치";
					break;

				case MaterialSourceKind.Combine:
					chip.text = "조합";
					break;

				default:
					chip.text = "분해";
					break;
			}
		}

		private SourceRowView getOrCreateSourceRow(int index)
		{
			if (index < _sourceRows.Count)
			{
				return _sourceRows[index];
			}

			SourceRowView row = new SourceRowView();
			row.root = new VisualElement();
			row.root.AddToClassList("material-source");
			_sourceList.Add(row.root);

			row.chip = new Label();
			row.chip.AddToClassList("material-source__chip");
			row.root.Add(row.chip);

			row.text = new Label();
			row.text.AddToClassList("material-source__text");
			row.root.Add(row.text);

			row.combine = new Button();
			row.combine.text = "조합하기";
			row.combine.AddToClassList("btn");
			row.combine.AddToClassList("btn--small");
			row.combine.AddToClassList("material-source__combine");
			row.combine.RegisterCallback<ClickEvent>(onCombineClicked);
			row.root.Add(row.combine);

			_sourceRows.Add(row);
			return row;
		}

		private UseRowView getOrCreateUseRow(int index)
		{
			if (index < _useRows.Count)
			{
				return _useRows[index];
			}

			UseRowView row = new UseRowView();
			row.root = new VisualElement();
			row.root.AddToClassList("material-use");
			row.root.AddToClassList("sfx-click");
			row.root.RegisterCallback<ClickEvent>(onUseRowClicked);
			_useList.Add(row.root);

			VisualElement cellHolder = new VisualElement();
			cellHolder.pickingMode = PickingMode.Ignore;
			cellHolder.AddToClassList("material-use__cell");
			row.root.Add(cellHolder);
			row.cell = new ItemCellView(cellHolder, _gradeColors, _icons);
			row.cell.Root.pickingMode = PickingMode.Ignore;	// 클릭은 줄이 받는다

			row.name = new Label();
			row.name.pickingMode = PickingMode.Ignore;
			row.name.AddToClassList("material-use__name");
			row.root.Add(row.name);

			row.count = new Label();
			row.count.pickingMode = PickingMode.Ignore;
			row.count.AddToClassList("material-use__count");
			row.root.Add(row.count);

			VisualElement arrow = new VisualElement();
			arrow.pickingMode = PickingMode.Ignore;
			arrow.AddToClassList("material-use__arrow");
			row.root.Add(arrow);

			_useRows.Add(row);
			return row;
		}

		private void onCombineClicked(ClickEvent evt)
		{
			_resultRecipeId = (int)((VisualElement)evt.currentTarget).userData;
			CloseFromInput();
		}

		// 사용처 줄 — 그 제작법을 돌려주며 닫는다.
		private void onUseRowClicked(ClickEvent evt)
		{
			_resultRecipeId = (int)((VisualElement)evt.currentTarget).userData;
			CloseFromInput();
		}

		private void onTabSourcesClicked()
		{
			selectTab(TAB_SOURCES);
		}

		private void onTabUsesClicked()
		{
			selectTab(TAB_USES);
		}

		// 읽기 전용이면 무엇이 와도 잠근다.
		private void applyControlsInteractable(bool plus, bool minus, bool use, bool delete)
		{
			bool allowed = (_readOnly == false);
			_plus.SetEnabled(plus && allowed);
			_minus.SetEnabled(minus && allowed);
			_use.SetEnabled(use && allowed);
			_delete.SetEnabled(delete && allowed);
		}

		private void onPlusClicked()
		{
			if (OnPlusClicked != null) { OnPlusClicked.Invoke(); }
		}

		private void onMinusClicked()
		{
			if (OnMinusClicked != null) { OnMinusClicked.Invoke(); }
		}

		private void onUseClicked()
		{
			if (OnUseClicked != null) { OnUseClicked.Invoke(); }
		}

		private void onDeleteClicked()
		{
			if (OnDeleteClicked != null) { OnDeleteClicked.Invoke(); }
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
