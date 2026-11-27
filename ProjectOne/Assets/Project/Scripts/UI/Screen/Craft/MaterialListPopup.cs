using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 보유 재료 팝업(UIPrefab_MaterialListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Material/MaterialListPopup.uxml.
	// 위는 재료 격자(개수 배지, 0개는 흐리게), 칸을 고르면 아래 상세 패널에 이름·보유량·획득처를 보인다.
	// 획득처 중 조합은 '조합하기' 로 팝업을 닫고 그 제작법을 돌려준다.
	public class MaterialListPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;

		private const string CELL_EMPTY = "material-cell--empty";
		private const string CELL_SELECTED = "material-cell--selected";
		private const string CHIP_PREFIX = "material-source__chip--";
		private const string ICON_PREFIX = "material-source__icon--";

		// 획득처 한 줄.
		private sealed class SourceRowView
		{
			public VisualElement root;
			public VisualElement icon;
			public Label chip;
			public Label text;
			public Button combine;
		}

		public event Action<int> OnSlotClicked;

		private readonly MaterialListPopupPresenter _presenter = new MaterialListPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<ItemCellView> _cells = new List<ItemCellView>();
		private readonly List<SourceRowView> _sourceRows = new List<SourceRowView>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private ScrollView _grid;
		private Label _detailName;
		private Label _detailOwned;
		private VisualElement _sourceList;
		private Label _sourceNone;
		private int _selectedIndex = -1;
		private int _resultRecipeId;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_grid = root.Q<ScrollView>("grid");
			_grid.AddManipulator(new ScrollDragManipulator(_grid));
			_detailName = root.Q<Label>("detail-name");
			_detailOwned = root.Q<Label>("detail-owned");
			_sourceList = root.Q("source-list");
			_sourceNone = root.Q<Label>("source-none");

			root.Q<Button>("close").clicked += Close;
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

		// 닫힐 때까지 기다린다. '조합하기' 로 닫으면 그 제작법 ID, 그냥 닫으면 0.
		public async UniTask<int> ShowAsync(CancellationToken ct)
		{
			_resultRecipeId = 0;
			await _presenter.ShowAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			return _resultRecipeId;
		}

		public async UniTask RenderAsync(IReadOnlyList<MaterialSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				ItemCellView cell = getOrCreateCell(i);
				cell.SetShown(true);
				cell.Tag = i;
				cell.Root.EnableInClassList(CELL_EMPTY, data[i].owned <= 0);
				cell.Root.RemoveFromClassList(CELL_SELECTED);
				_bindTasks.Add(cell.BindItemAsync(EDT.Table_Item.Get(data[i].itemId), data[i].owned, ct));
				cell.SetCountText(data[i].owned.ToString());
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			_selectedIndex = -1;
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 고른 칸 강조 + 상세 패널.
		public void ShowDetail(MaterialSlotData data, int index)
		{
			if (_selectedIndex >= 0 && _selectedIndex < _cells.Count)
			{
				_cells[_selectedIndex].Root.RemoveFromClassList(CELL_SELECTED);
			}

			_selectedIndex = index;
			_cells[index].Root.AddToClassList(CELL_SELECTED);

			_detailName.text = data.name;
			_detailOwned.text = "보유 " + data.owned;

			for (int i = 0; i < data.sources.Count; i++)
			{
				SourceRowView row = getOrCreateSourceRow(i);
				MaterialSourceData source = data.sources[i];
				row.root.style.display = DisplayStyle.Flex;
				applyChip(row.chip, source.kind);
				applyIcon(row.icon, source.kind);
				row.text.text = source.text;
				row.combine.userData = source.recipeId;
				row.combine.style.display = (source.kind == MaterialSourceKind.Combine) ? DisplayStyle.Flex : DisplayStyle.None;
			}

			for (int i = data.sources.Count; i < _sourceRows.Count; i++)
			{
				_sourceRows[i].root.style.display = DisplayStyle.None;
			}

			_sourceNone.style.display = (data.sources.Count == 0) ? DisplayStyle.Flex : DisplayStyle.None;
		}

		public void Reveal()
		{
			PlayPopupOpen();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private static void applyChip(Label chip, MaterialSourceKind kind)
		{
			chip.EnableInClassList(CHIP_PREFIX + "boss", kind == MaterialSourceKind.Boss);
			chip.EnableInClassList(CHIP_PREFIX + "combine", kind == MaterialSourceKind.Combine);
			chip.EnableInClassList(CHIP_PREFIX + "decompose", kind == MaterialSourceKind.Decompose);
			switch (kind)
			{
				case MaterialSourceKind.Boss:
					chip.text = "보스 처치";
					break;

				case MaterialSourceKind.Combine:
					chip.text = "조합";
					break;

				default:
					chip.text = "분해 환급";
					break;
			}
		}

		// 획득처 아이콘 — 모양·색은 Material.uss 의 종류별 클래스가 정한다.
		private static void applyIcon(VisualElement icon, MaterialSourceKind kind)
		{
			icon.EnableInClassList(ICON_PREFIX + "boss", kind == MaterialSourceKind.Boss);
			icon.EnableInClassList(ICON_PREFIX + "combine", kind == MaterialSourceKind.Combine);
			icon.EnableInClassList(ICON_PREFIX + "decompose", kind == MaterialSourceKind.Decompose);
		}

		private ItemCellView getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			ItemCellView cell = new ItemCellView(_grid.contentContainer, _gradeColors, _icons);
			cell.Root.AddToClassList("material-cell");
			cell.OnClicked += onCellClicked;
			_cells.Add(cell);
			return cell;
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

			row.icon = new VisualElement();
			row.icon.pickingMode = PickingMode.Ignore;
			row.icon.AddToClassList("material-source__icon");
			row.root.Add(row.icon);

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

		private void onCellClicked(ItemCellView sender, long uid, int itemId)
		{
			if (OnSlotClicked != null)
			{
				OnSlotClicked.Invoke((int)sender.Tag);
			}
		}

		private void onCombineClicked(ClickEvent evt)
		{
			_resultRecipeId = (int)((VisualElement)evt.currentTarget).userData;
			Close();
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
