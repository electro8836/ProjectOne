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
	// 탭 없이 한 스크롤 — 등급마다 머리띠(등급색 띠 · 이름 · 종 수 · 합계) 아래에 나올 수 있는 아이템 칸을 깔고, 칸 아래띠에 확률을 적는다.
	public class BoxRewardPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		// 아이템 칸 + 확률 1칸.
		private sealed class PoolCell
		{
			public VisualElement root;
			public ItemCellView cell;
			public Label chance;
		}

		// 등급 구역 1개 — 머리띠 + 칸 격자.
		private sealed class Group
		{
			public VisualElement root;
			public VisualElement bar;
			public Label name;
			public Label count;
			public Label total;
			public VisualElement grid;
			public readonly List<PoolCell> cells = new List<PoolCell>();
		}

		private readonly BoxRewardPresenter _presenter = new BoxRewardPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<Group> _groups = new List<Group>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private ScrollView _grid;
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

		public async UniTask ShowAsync(int rewardGroupId, CancellationToken ct)
		{
			await _presenter.ShowAsync(rewardGroupId, ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 등급마다 구역을 그린다. 확률은 전체 기준 절대값이라 구역 합계를 모두 더하면 100% 다.
		public async UniTask RenderAsync(IReadOnlyList<ItemGradeType> grades, IReadOnlyList<RewardChance> chances, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int g = 0; g < grades.Count; g++)
			{
				ItemGradeType grade = grades[g];
				Group group = getOrCreateGroup(g);
				group.root.style.display = DisplayStyle.Flex;

				int count = 0;
				float total = 0f;
				for (int i = 0; i < chances.Count; i++)
				{
					if (chances[i].grade != grade)
					{
						continue;
					}

					PoolCell pool = getOrCreateCell(group, count);
					pool.root.style.display = DisplayStyle.Flex;
					pool.chance.text = toPercent(chances[i].chance);
					_bindTasks.Add(bindCell(pool.cell, chances[i], ct));
					total += chances[i].chance;
					count++;
				}

				for (int i = count; i < group.cells.Count; i++)
				{
					group.cells[i].root.style.display = DisplayStyle.None;
				}

				applyHead(group, grade, count, total);
			}

			for (int g = grades.Count; g < _groups.Count; g++)
			{
				_groups[g].root.style.display = DisplayStyle.None;
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

		private void applyHead(Group group, ItemGradeType grade, int count, float total)
		{
			ItemGradeColorTable.GradeColor colors = _gradeColors.Get(grade);
			group.bar.style.backgroundColor = colors.border;
			group.name.text = ItemGradeNames.Get(grade);
			group.name.style.color = colors.text;
			group.count.text = count + "종";
			group.total.text = "합계 " + toPercent(total);
		}

		private static string toPercent(float chance)
		{
			return (chance * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";
		}

		// 칸은 장비든 아니든 아이템으로 그린다 — 확률표는 레벨·품질이 의미가 없다. 틀 색은 테이블 등급이 아니라 나오는 등급이다.
		private static UniTask bindCell(ItemCellView cell, RewardChance chance, CancellationToken ct)
		{
			Table_Item.Row row = (chance.equipment != null) ? chance.equipment.Item : Table_Item.Get(chance.itemId);
			if (row == null)
			{
				cell.SetShown(false);
				return UniTask.CompletedTask;
			}

			cell.SetShown(true);
			UniTask task = cell.BindItemAsync(row, 0, ct);
			cell.SetGrade(chance.grade);
			return task;
		}

		private Group getOrCreateGroup(int index)
		{
			if (index < _groups.Count)
			{
				return _groups[index];
			}

			Group group = new Group();
			group.root = addElement(_grid.contentContainer, "box-group");

			VisualElement head = addElement(group.root, "box-group__head");
			group.bar = addElement(head, "box-group__bar");
			group.name = addLabel(head, "box-group__name");
			group.count = addLabel(head, "box-group__count");
			group.total = addLabel(head, "box-group__total");

			group.grid = addElement(group.root, "box-group__grid");

			_groups.Add(group);
			return group;
		}

		private PoolCell getOrCreateCell(Group group, int index)
		{
			if (index < group.cells.Count)
			{
				return group.cells[index];
			}

			PoolCell pool = new PoolCell();
			pool.root = addElement(group.grid, "pool-cell");
			pool.cell = new ItemCellView(pool.root, _gradeColors, _icons);
			pool.chance = addLabel(pool.root, "pool-cell__chance");

			group.cells.Add(pool);
			return pool;
		}

		private static VisualElement addElement(VisualElement parent, string className)
		{
			VisualElement element = new VisualElement();
			element.AddToClassList(className);
			parent.Add(element);
			return element;
		}

		private static Label addLabel(VisualElement parent, string className)
		{
			Label label = new Label();
			label.AddToClassList(className);
			label.pickingMode = PickingMode.Ignore;
			parent.Add(label);
			return label;
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
