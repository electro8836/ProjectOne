using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 보유 재화 목록 팝업(UIPrefab_CurrencyListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Currency/CurrencyListPopup.uxml.
	// 재화마다 아이템 칸(아이콘 + 아래띠 보유량)을 그리고, 칸을 누르면 Presenter 가 그 칸 위에 이름·설명 툴팁을 띄운다.
	public class CurrencyListPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO — 재화 칸은 일반 등급 틀

		// 재화 1칸 — 아이템 칸 + 재화 종류.
		private sealed class CurrencyCell
		{
			public ItemCellView view;
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
				cell.view.SetShown(true);
				cell.currency = data[i].currency;
				_bindTasks.Add(cell.view.BindCurrencyAsync(data[i].currency, data[i].amount, ct));
				cell.view.SetCountText(CurrencyFormat.Short(data[i].amount));
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].view.SetShown(false);
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
					_cells[i].view.SetCountText(CurrencyFormat.Short(amount));
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
			cell.view = new ItemCellView(_grid.contentContainer, _gradeColors, _icons);
			cell.view.Root.AddToClassList("currency-cell");
			cell.view.OnClicked += onCellClicked;

			_cells.Add(cell);
			return cell;
		}

		// 재화 칸은 itemId 자리에 재화 종류를 넘긴다(ItemCellView.BindCurrencyAsync).
		private void onCellClicked(ItemCellView sender, long uid, int itemId)
		{
			if (OnSlotClicked != null)
			{
				OnSlotClicked.Invoke((EDT.Currency)itemId, sender.Root);
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
