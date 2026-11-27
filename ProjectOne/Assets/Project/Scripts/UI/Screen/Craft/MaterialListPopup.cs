using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 보유 재료 팝업(UIPrefab_MaterialListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Material/MaterialListPopup.uxml.
	// 재료 격자(개수 배지, 0개는 흐리게)만 보이고, 칸을 누르면 아이템 정보 팝업(설명·획득처)이 뜬다.
	// 그 팝업에서 '조합하기' 를 누르면 이 팝업도 닫히며 그 제작법을 돌려준다.
	public class MaterialListPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;

		private const string CELL_EMPTY = "material-cell--empty";

		public event Action<int> OnSlotClicked;

		private readonly MaterialListPopupPresenter _presenter = new MaterialListPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<ItemCellView> _cells = new List<ItemCellView>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private ScrollView _grid;
		private int _resultRecipeId;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_grid = root.Q<ScrollView>("grid");
			_grid.AddManipulator(new ScrollDragManipulator(_grid));

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

		// 닫힐 때까지 기다린다. '조합하기'나 사용처 줄로 닫히면 그 제작법 ID, 그냥 닫으면 0.
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
				_bindTasks.Add(cell.BindItemAsync(EDT.Table_Item.Get(data[i].itemId), data[i].owned, ct));
				cell.SetCountText(data[i].owned.ToString());
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
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

		// 아이템 정보 팝업의 '조합하기' — 그 제작법을 돌려주며 닫는다.
		public void CloseWithRecipe(int recipeId)
		{
			_resultRecipeId = recipeId;
			Close();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

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

		private void onCellClicked(ItemCellView sender, long uid, int itemId)
		{
			if (OnSlotClicked != null)
			{
				OnSlotClicked.Invoke((int)sender.Tag);
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
