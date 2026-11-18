using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 상자 확률표 팝업(UIPrefab_BoxRewardPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/BoxReward/BoxRewardPopup.uxml.
	// 등급별 탭으로 나눠, 나올 수 있는 아이템 칸 아래에 확률을 적는다.
	public class BoxRewardPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action<int> OnTabSelected;	// 탭 인덱스

		// 아이템 칸 + 확률 1칸.
		private sealed class PoolCell
		{
			public VisualElement root;
			public ItemCellView cell;
			public Label chance;
		}

		private readonly BoxRewardPresenter _presenter = new BoxRewardPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<PoolCell> _cells = new List<PoolCell>();
		private readonly List<Button> _tabs = new List<Button>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private VisualElement _tabBar;
		private ScrollView _grid;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_tabBar = root.Q("tabs");
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

		public async UniTask ShowAsync(int rewardGroupId, CancellationToken ct)
		{
			await _presenter.ShowAsync(rewardGroupId, ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 등급 수만큼 탭을 만들고 등급 이름을 단다.
		public void SetGrades(IReadOnlyList<ItemGradeType> grades)
		{
			for (int i = 0; i < grades.Count; i++)
			{
				Button tab = getTab(i);
				tab.text = ItemGradeNames.Get(grades[i]);
				tab.style.display = DisplayStyle.Flex;
			}

			for (int i = grades.Count; i < _tabs.Count; i++)
			{
				_tabs[i].style.display = DisplayStyle.None;
			}
		}

		// 탭만 선택(OnTabSelected 를 발행하지 않음).
		public void SelectTab(int index)
		{
			for (int i = 0; i < _tabs.Count; i++)
			{
				_tabs[i].EnableInClassList("tab--selected", i == index);
			}
		}

		public async UniTask RenderAsync(IReadOnlyList<RewardChance> chances, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < chances.Count; i++)
			{
				PoolCell pool = getOrCreateCell(i);
				pool.root.style.display = DisplayStyle.Flex;
				pool.chance.text = (chances[i].chance * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";
				_bindTasks.Add(bindCell(pool.cell, chances[i], ct));
			}

			for (int i = chances.Count; i < _cells.Count; i++)
			{
				_cells[i].root.style.display = DisplayStyle.None;
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
			_grid.scrollOffset = Vector2.zero;
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

		private static UniTask bindCell(ItemCellView cell, RewardChance chance, CancellationToken ct)
		{
			if (chance.equipment != null)
			{
				return cell.BindEquipmentAsync(chance.equipment, false, ct);
			}

			Table_Item.Row row = Table_Item.Get(chance.itemId);
			if (row == null)
			{
				cell.SetShown(false);
				return UniTask.CompletedTask;
			}

			cell.SetShown(true);
			return cell.BindItemAsync(row, 0, ct);
		}

		private Button getTab(int index)
		{
			if (index < _tabs.Count)
			{
				return _tabs[index];
			}

			Button tab = new Button();
			tab.AddToClassList("tab");
			tab.userData = index;
			tab.RegisterCallback<ClickEvent>(onTabClicked);
			_tabBar.Add(tab);
			_tabs.Add(tab);
			return tab;
		}

		private PoolCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			PoolCell pool = new PoolCell();
			pool.root = new VisualElement();
			pool.root.AddToClassList("pool-cell");
			pool.cell = new ItemCellView(pool.root, _gradeColors, _icons);
			pool.chance = new Label();
			pool.chance.AddToClassList("pool-cell__chance");
			pool.root.Add(pool.chance);
			_grid.contentContainer.Add(pool.root);

			_cells.Add(pool);
			return pool;
		}

		private void onTabClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			SelectTab(index);

			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke(index);
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
