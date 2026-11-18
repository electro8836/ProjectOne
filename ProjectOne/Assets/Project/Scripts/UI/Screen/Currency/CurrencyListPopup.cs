using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 보유 재화 목록 팝업(UIPrefab_CurrencyListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Currency/CurrencyListPopup.uxml.
	// 재화마다 아이콘 + 보유량 칸을 그리고, 칸을 누르면 Presenter 가 그 칸 위에 설명 툴팁을 띄운다.
	public class CurrencyListPopup : ToolkitScreen, IView
	{
		// 재화 1칸 — 아이콘 + 수량.
		private sealed class CurrencyCell
		{
			public VisualElement root;
			public VisualElement icon;
			public Label count;
			public EDT.Currency currency;
		}

		public event Action<EDT.Currency, VisualElement> OnSlotClicked;

		private readonly CurrencyListPopupPresenter _presenter = new CurrencyListPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<CurrencyCell> _cells = new List<CurrencyCell>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private ScrollView _grid;
		private int _visibleCount;	// 켜져 있는 칸 수 — 수량 갱신 시 꺼진 칸을 건드리지 않는다
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

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			await _presenter.ShowAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public async UniTask RenderAsync(IReadOnlyList<CurrencySlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				CurrencyCell cell = getOrCreateCell(i);
				cell.root.style.display = DisplayStyle.Flex;
				cell.currency = data[i].currency;
				cell.count.text = CurrencySlot.FormatAmount(data[i].amount);
				_bindTasks.Add(_icons.SetAsync(cell.icon, data[i].iconAddress, ct));
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].root.style.display = DisplayStyle.None;
			}

			_visibleCount = data.Count;
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		public void UpdateAmount(EDT.Currency currency, int amount)
		{
			for (int i = 0; i < _visibleCount; i++)
			{
				if (_cells[i].currency == currency)
				{
					_cells[i].count.text = CurrencySlot.FormatAmount(amount);
					return;
				}
			}
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

		private CurrencyCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			CurrencyCell cell = new CurrencyCell();
			cell.root = new VisualElement();
			cell.root.AddToClassList("currency-cell");
			cell.root.userData = cell;
			cell.root.RegisterCallback<ClickEvent>(onCellClicked);
			cell.root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			cell.icon = new VisualElement();
			cell.icon.AddToClassList("currency-cell__icon");
			cell.icon.pickingMode = PickingMode.Ignore;
			cell.root.Add(cell.icon);

			cell.count = new Label();
			cell.count.AddToClassList("currency-cell__count");
			cell.count.pickingMode = PickingMode.Ignore;
			cell.root.Add(cell.count);

			_grid.contentContainer.Add(cell.root);
			_cells.Add(cell);
			return cell;
		}

		private void onCellClicked(ClickEvent evt)
		{
			CurrencyCell cell = ((VisualElement)evt.currentTarget).userData as CurrencyCell;
			if (cell != null && OnSlotClicked != null)
			{
				OnSlotClicked.Invoke(cell.currency, cell.root);
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
