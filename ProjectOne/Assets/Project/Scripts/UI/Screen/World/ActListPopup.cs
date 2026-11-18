using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 구역 목록 팝업(UIPrefab_ActListPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/ActList/ActListPopup.uxml.
	// 월드 탭을 고르면 그 월드의 구역 줄을 그린다. 갈 수 있는 다른 구역 줄에만 이동 버튼이 있고,
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

		public event Action<int> OnTabSelected;		// 탭 인덱스
		public event Action<int> OnMoveRequested;	// 목적지 Field.ID
		public event Action OnTownRequested;		// 마을 귀환

		private readonly ActListPopupPresenter _presenter = new ActListPopupPresenter();
		private readonly List<FieldRow> _rows = new List<FieldRow>();
		private readonly List<Button> _tabs = new List<Button>();

		private VisualElement _tabBar;
		private Label _actName;
		private ScrollView _list;
		private Button _townButton;
		private int _scrollIndex = -1;
		private UniTaskCompletionSource<bool> _tcs;

		protected override void OnBind(VisualElement root)
		{
			_tabBar = root.Q("tabs");
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

		// 월드 수만큼 탭을 만든다.
		public void SetTabCount(int count)
		{
			for (int i = 0; i < count; i++)
			{
				Button tab = getTab(i);
				tab.style.display = DisplayStyle.Flex;
			}

			for (int i = count; i < _tabs.Count; i++)
			{
				_tabs[i].style.display = DisplayStyle.None;
			}
		}

		public void SetTownButtonVisible(bool visible)
		{
			_townButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 탭만 선택(OnTabSelected 를 발행하지 않음).
		public void SelectTab(int index)
		{
			for (int i = 0; i < _tabs.Count; i++)
			{
				_tabs[i].EnableInClassList("tab--selected", i == index);
			}
		}

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

		private Button getTab(int index)
		{
			if (index < _tabs.Count)
			{
				return _tabs[index];
			}

			Button tab = new Button();
			tab.text = "월드 " + (index + 1).ToString();
			tab.AddToClassList("tab");
			tab.userData = index;
			tab.RegisterCallback<ClickEvent>(onTabClicked);
			_tabBar.Add(tab);
			_tabs.Add(tab);
			return tab;
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
			row.move.AddToClassList("btn");
			row.move.AddToClassList("btn--green");
			row.move.AddToClassList("btn--small");
			row.move.userData = row;
			row.move.RegisterCallback<ClickEvent>(onMoveClicked);
			row.move.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
			row.root.Add(row.move);

			_list.contentContainer.Add(row.root);
			_rows.Add(row);
			return row;
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
