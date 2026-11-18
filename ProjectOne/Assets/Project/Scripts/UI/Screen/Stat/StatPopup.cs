using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 내 캐릭터 능력치 팝업(UIPrefab_StatPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Stat/StatPopup.uxml.
	// UIManager.ShowStatPopupAsync 가 열고 닫힘을 기다린다. 값 계산은 StatPopupPresenter 가 하고,
	// 아이콘까지 준비되면 Reveal 로 한 번에 보인다(빈 줄이 깜빡이지 않게).
	public class StatPopup : ToolkitScreen, IView
	{
		// 능력치 한 줄 — 아이콘 + 값 문구.
		private sealed class Row
		{
			public VisualElement root;
			public VisualElement icon;
			public Label text;
		}

		private readonly StatPopupPresenter _presenter = new StatPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<Row> _offensiveRows = new List<Row>();
		private readonly List<Row> _defensiveRows = new List<Row>();
		private readonly List<Row> _utilityRows = new List<Row>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private VisualElement _offensive;
		private VisualElement _defensive;
		private VisualElement _utility;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_offensive = root.Q("offensive");
			_defensive = root.Q("defensive");
			_utility = root.Q("utility");

			ScrollView scroll = root.Q<ScrollView>("scroll");
			scroll.AddManipulator(new ScrollDragManipulator(scroll));

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

		public async UniTask RenderAsync(IReadOnlyList<StatSlotData> offensive, IReadOnlyList<StatSlotData> defensive,
			IReadOnlyList<StatSlotData> utility, CancellationToken ct)
		{
			_bindTasks.Clear();
			renderGroup(_offensiveRows, _offensive, offensive, ct);
			renderGroup(_defensiveRows, _defensive, defensive, ct);
			renderGroup(_utilityRows, _utility, utility, ct);
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 첫 렌더가 끝나면 보인다.
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

		private void renderGroup(List<Row> rows, VisualElement parent, IReadOnlyList<StatSlotData> data, CancellationToken ct)
		{
			for (int i = 0; i < data.Count; i++)
			{
				Row row = getOrCreateRow(rows, parent, i);
				row.root.style.display = DisplayStyle.Flex;
				row.text.text = data[i].text;
				_bindTasks.Add(_icons.SetAsync(row.icon, data[i].iconAddress, ct));
			}

			for (int i = data.Count; i < rows.Count; i++)
			{
				rows[i].root.style.display = DisplayStyle.None;
			}
		}

		private static Row getOrCreateRow(List<Row> rows, VisualElement parent, int index)
		{
			if (index < rows.Count)
			{
				return rows[index];
			}

			Row row = new Row();
			row.root = new VisualElement();
			row.root.AddToClassList("stat-row");
			row.icon = new VisualElement();
			row.icon.AddToClassList("stat-row__icon");
			row.text = new Label();
			row.text.AddToClassList("stat-row__text");
			row.root.Add(row.icon);
			row.root.Add(row.text);
			parent.Add(row.root);

			rows.Add(row);
			return row;
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
