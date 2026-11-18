using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 펫 목록 창의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/Screens/Pet/PetScreen.uxml.
	// 장비창의 펫 버튼이 창 층에 겹쳐 연다. 칸을 누르면 Presenter 가 펫 강화 팝업을 띄운다.
	public class PetInfoUI : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action<int> OnSlotClicked;
		public event Action OnSortClicked;
		public event Action OnReturnClicked;

		private readonly PetInfoPresenter _presenter = new PetInfoPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<PetCell> _cells = new List<PetCell>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private Label _title;
		private Button _sortButton;
		private ScrollView _list;

		// Presenter 가 등급 색을 정해야 해서 열어 둔다.
		public ItemGradeColorTable GradeColors
		{
			get { return _gradeColors; }
		}

		public override bool HidesNavigationBar
		{
			get { return true; }
		}

		protected override void OnBind(VisualElement root)
		{
			_title = root.Q<Label>("title");
			_sortButton = root.Q<Button>("sort-button");
			_sortButton.clicked += onSortClicked;
			root.Q<Button>("return-button").clicked += onReturnClicked;

			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));

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

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public void SetTitle(int owned, int total)
		{
			_title.text = $"펫 ({owned} / {total})";
		}

		public void SetSortLabel(string label)
		{
			_sortButton.text = label;
		}

		public async UniTask RenderListAsync(IReadOnlyList<PetSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				PetCell cell = getOrCreateCell(i);
				cell.SetShown(true);
				_bindTasks.Add(cell.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private PetCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			PetCell cell = new PetCell(_list.contentContainer, _icons);
			cell.OnClicked += onCellClicked;
			_cells.Add(cell);
			return cell;
		}

		private void onCellClicked(PetCell sender)
		{
			if (OnSlotClicked != null)
			{
				OnSlotClicked.Invoke(sender.PetId);
			}
		}

		private void onSortClicked()
		{
			if (OnSortClicked != null)
			{
				OnSortClicked.Invoke();
			}
		}

		private void onReturnClicked()
		{
			if (OnReturnClicked != null)
			{
				OnReturnClicked.Invoke();
			}
		}
	}
}
