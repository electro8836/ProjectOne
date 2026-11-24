using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 월드맵(구역 목록) 팝업(UIPrefab_ActListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/ActList/ActListPopup.uxml.
	// ◀ 월드 이름 ▶ 선택기로 월드를 고르면 그 월드의 구역 줄을 그린다. 월드가 계속 늘어나도 선택기 폭은 같고,
	// 몇 번째 월드인지는 아래 점으로 보인다. 갈 수 있는 다른 구역 줄에만 이동 버튼이 있고,
	// 이동하거나 마을로 돌아가면 이동했다(true)로 닫힌다.
	public class ActListPopup : ToolkitScreen, IView
	{
		// 구역 한 줄 — 번호·이름·이동 버튼. 지금 있는 곳은 강조, 아직 못 가는 곳은 흐리게.
		private sealed class FieldRow
		{
			public VisualElement root;
			public Label order;
			public Label name;
			public Button move;
			public int fieldId;
		}

		public event Action<int> OnTabSelected;		// 월드 인덱스
		public event Action<int> OnMoveRequested;	// 목적지 Field.ID
		public event Action OnTownRequested;		// 마을 귀환

		private readonly ActListPopupPresenter _presenter = new ActListPopupPresenter();
		private readonly List<FieldRow> _rows = new List<FieldRow>();
		private readonly List<VisualElement> _dots = new List<VisualElement>();

		private VisualElement _dotBar;
		private Button _prevButton;
		private Button _nextButton;
		private Label _actName;
		private ScrollView _list;
		private Button _townButton;
		private int _worldCount;
		private int _selected;
		private int _scrollIndex = -1;
		private UniTaskCompletionSource<bool> _tcs;

		protected override void OnBind(VisualElement root)
		{
			_dotBar = root.Q("dots");
			_prevButton = root.Q<Button>("prev");
			_prevButton.clicked += onPrevClicked;
			_nextButton = root.Q<Button>("next");
			_nextButton.clicked += onNextClicked;
			_actName = root.Q<Label>("act-name");
			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));

			_townButton = root.Q<Button>("town");
			_townButton.clicked += onTownClicked;
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

			base.OnDestroy();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		public async UniTask<bool> ShowAsync(CancellationToken ct)
		{
			await _presenter.OnOpenAsync(ct);

			_tcs = new UniTaskCompletionSource<bool>();
			PlayPopupOpen();

			(bool cancelled, bool moved) = await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return false;
			}

			return moved;
		}

		// 월드 수만큼 점을 만든다.
		public void SetTabCount(int count)
		{
			_worldCount = count;
			for (int i = 0; i < count; i++)
			{
				getDot(i).style.display = DisplayStyle.Flex;
			}

			for (int i = count; i < _dots.Count; i++)
			{
				_dots[i].style.display = DisplayStyle.None;
			}
		}

		public void SetTownButtonVisible(bool visible)
		{
			_townButton.parent.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;	// 버튼 줄째로 — 아래 여백까지 접는다
		}

		// 월드만 선택(OnTabSelected 를 발행하지 않음) — 점과 화살표 잠금을 맞춘다. 첫·마지막 월드면 그쪽 화살표를 잠근다.
		public void SelectTab(int index)
		{
			_selected = index;
			for (int i = 0; i < _dots.Count; i++)
			{
				_dots[i].EnableInClassList("world-select__dot--on", i == index);
			}

			_prevButton.SetEnabled(index > 0);
			_nextButton.SetEnabled(index < _worldCount - 1);
		}

		// 선택기 가운데 월드 이름.
		public void SetActName(string text)
		{
			_actName.text = text;
		}

		public void RenderFields(IReadOnlyList<FieldSlotData> data)
		{
			for (int i = 0; i < data.Count; i++)
			{
				FieldRow row = getOrCreateRow(i);
				FieldSlotData d = data[i];

				row.root.style.display = DisplayStyle.Flex;
				row.fieldId = d.fieldId;
				row.order.text = d.order.ToString();
				row.name.text = d.name;

				row.root.EnableInClassList("field-row--current", d.current);
				row.root.EnableInClassList("field-row--locked", d.cleared == false && d.current == false);

				// 이동 버튼은 "갈 수 있는 다른 곳" 에만 둔다.
				row.move.style.display = (d.cleared == true && d.current == false) ? DisplayStyle.Flex : DisplayStyle.None;
			}

			for (int i = data.Count; i < _rows.Count; i++)
			{
				_rows[i].root.style.display = DisplayStyle.None;
			}
		}

		// 지정한 줄이 보이도록 스크롤한다. 배치가 끝난 다음 프레임에 옮긴다.
		public void ScrollToSlot(int index)
		{
			if (index < 0 || index >= _rows.Count)
			{
				return;
			}

			_scrollIndex = index;
			_list.schedule.Execute(applyScroll);
		}

		public void Close()
		{
			close(false);
		}

		public void CloseByMove()
		{
			close(true);
		}

		// ── 내부 ─────────────────────────────────────────────────────────

		private void applyScroll()
		{
			if (_scrollIndex >= 0 && _scrollIndex < _rows.Count)
			{
				_list.ScrollTo(_rows[_scrollIndex].root);
			}
		}

		private VisualElement getDot(int index)
		{
			if (index < _dots.Count)
			{
				return _dots[index];
			}

			VisualElement dot = new VisualElement();
			dot.AddToClassList("world-select__dot");
			dot.pickingMode = PickingMode.Ignore;
			_dotBar.Add(dot);
			_dots.Add(dot);
			return dot;
		}

		private FieldRow getOrCreateRow(int index)
		{
			if (index < _rows.Count)
			{
				return _rows[index];
			}

			FieldRow row = new FieldRow();
			row.root = new VisualElement();
			row.root.AddToClassList("field-row");

			row.order = new Label();
			row.order.AddToClassList("field-row__order");
			row.root.Add(row.order);

			row.name = new Label();
			row.name.AddToClassList("field-row__name");
			row.root.Add(row.name);

			row.move = new Button();
			row.move.text = "이동";
			row.move.AddToClassList("field-row__move");
			row.move.userData = row;
			row.move.RegisterCallback<ClickEvent>(onMoveClicked);
			row.move.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
			row.root.Add(row.move);

			_list.contentContainer.Add(row.root);
			_rows.Add(row);
			return row;
		}

		private void onPrevClicked()
		{
			selectByArrow(_selected - 1);
		}

		private void onNextClicked()
		{
			selectByArrow(_selected + 1);
		}

		private void selectByArrow(int index)
		{
			if (index < 0 || index >= _worldCount)
			{
				return;
			}

			SelectTab(index);

			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke(index);
			}
		}

		private void onMoveClicked(ClickEvent evt)
		{
			FieldRow row = ((VisualElement)evt.currentTarget).userData as FieldRow;
			if (row != null && OnMoveRequested != null)
			{
				OnMoveRequested.Invoke(row.fieldId);
			}
		}

		private void onTownClicked()
		{
			if (OnTownRequested != null)
			{
				OnTownRequested.Invoke();
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}

		private void close(bool moved)
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(moved);
			}
		}
	}
}
