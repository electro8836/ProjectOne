using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.HeroPasses;

namespace ProjectOne.UI
{
	// 히어로 패스 팝업(UIPrefab_PassPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/HeroPass/HeroPassPopup.uxml.
	// 위에 패스 레벨·구매 상태·진행도·남은 기간, 아래에 레벨별 보상 줄(일반 | 레벨 | 패스).
	// 남은 기간은 Presenter 가 매초(OnSecondTick) 갱신한다.
	public class HeroPassPopup : ToolkitScreen, IView
	{
		private const long TickIntervalMs = 1000;

		private readonly HeroPassPopupPresenter _presenter = new HeroPassPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<HeroPassRow> _rows = new List<HeroPassRow>(50);
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private Label _level;
		private Label _state;
		private Label _remainTime;
		private VisualElement _progressFill;
		private Label _progressText;
		private VisualElement _header;
		private VisualElement _columns;
		private VisualElement _board;
		private VisualElement _guide;
		private VisualElement _scrollThumb;
		private ScrollView _list;
		private Button _allReceiveButton;
		private IVisualElementScheduledItem _tick;
		private int _scrollIndex = -1;
		private UniTaskCompletionSource _tcs;

		public event Action OnSecondTick;
		public event Action<int, bool> OnClaimRequested;
		public event Action OnAllReceiveRequested;

		protected override void OnBind(VisualElement root)
		{
			_level = root.Q<Label>("level");
			_state = root.Q<Label>("state");
			_remainTime = root.Q<Label>("remain");
			_progressFill = root.Q("progress-fill");
			_progressText = root.Q<Label>("progress-text");
			_header = root.Q("header");
			_columns = root.Q("columns");
			_guide = root.Q("guide");
			_scrollThumb = root.Q("scroll-thumb");

			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));
			_list.verticalScroller.valueChanged += onScrollChanged;
			_list.RegisterCallback<GeometryChangedEvent>(onListGeometryChanged);
			_list.contentContainer.RegisterCallback<GeometryChangedEvent>(onListGeometryChanged);

			// 영역 배경 — 줄보다 먼저 넣어 맨 뒤에 깔고, 스크롤 내용과 함께 움직인다
			_board = new VisualElement();
			_board.AddToClassList("pass-board");
			_board.pickingMode = PickingMode.Ignore;
			_board.Add(createArea("pass-board__area--normal"));
			_board.Add(createArea("pass-board__area--pass"));
			_list.contentContainer.Add(_board);

			// 레벨 수만큼 줄을 미리 만든다(최대 레벨이 곧 줄 수).
			CancellationToken ownerToken = GetDestroyToken();
			for (int i = 0; i < HeroPassCatalog.MaxLevel; i++)
			{
				HeroPassRow row = new HeroPassRow(_list.contentContainer, _icons, ownerToken);
				row.OnClaimClicked += onRowClaimClicked;
				_rows.Add(row);
			}

			_allReceiveButton = root.Q<Button>("all-receive");
			_allReceiveButton.clicked += onAllReceiveClicked;
			root.Q<Button>("close").clicked += Close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);

			_tick = root.schedule.Execute(onTick).Every(TickIntervalMs);
			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_tick.Pause();
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

		// 패스 레벨과 구매 상태 — 구매하면 머리 영역을 강조한다.
		public void SetHeader(int level, bool isPurchased)
		{
			_level.text = level.ToString();
			_state.text = isPurchased ? "활성화됨" : "비활성화";
			_header.EnableInClassList("pass-header--active", isPurchased);

			// 미구매 — 패스 머리판 회색 + 패스 영역 어둡게 + 목록 아래 '구매하고 받기' 안내
			_columns.EnableInClassList("pass-columns--locked", isPurchased == false);
			_board.EnableInClassList("pass-board--locked", isPurchased == false);
			_guide.EnableInClassList("pass-guide--shown", isPurchased == false);
		}

		public void SetProgress(float ratio, string text)
		{
			_progressFill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);
			_progressText.text = text;
		}

		public void SetAllReceiveInteractable(bool value)
		{
			_allReceiveButton.SetEnabled(value);
		}

		public void SetRemainTimeText(string text)
		{
			_remainTime.text = text;
		}

		public async UniTask RenderSlotsAsync(IReadOnlyList<HeroPassRewardSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				if (i >= data.Count)
				{
					_rows[i].SetShown(false);
					continue;
				}

				_rows[i].SetShown(true);
				_bindTasks.Add(_rows[i].BindAsync(data[i], ct));
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
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

		private void applyScroll()
		{
			if (_scrollIndex >= 0 && _scrollIndex < _rows.Count)
			{
				_list.ScrollTo(_rows[_scrollIndex].Root);
			}
		}

		private VisualElement createArea(string className)
		{
			VisualElement area = new VisualElement();
			area.AddToClassList("pass-board__area");
			area.AddToClassList(className);
			area.pickingMode = PickingMode.Ignore;
			return area;
		}

		private void onScrollChanged(float value)
		{
			updateScrollThumb();
		}

		private void onListGeometryChanged(GeometryChangedEvent evt)
		{
			updateScrollThumb();
		}

		// 오른쪽 얇은 막대 — 보이는 비율만큼 길이, 스크롤 위치만큼 내려간다
		private void updateScrollThumb()
		{
			float view = _list.contentViewport.layout.height;
			float content = _list.contentContainer.layout.height;
			if (float.IsNaN(view) || float.IsNaN(content) || content <= view)
			{
				_scrollThumb.style.display = DisplayStyle.None;
				return;
			}

			float thumb = view * view / content;
			float high = _list.verticalScroller.highValue;
			float ratio = high > 0f ? Mathf.Clamp01(_list.verticalScroller.value / high) : 0f;
			_scrollThumb.style.display = DisplayStyle.Flex;
			_scrollThumb.style.height = thumb;
			_scrollThumb.style.top = (view - thumb) * ratio;
		}

		private void onTick()
		{
			if (OnSecondTick != null)
			{
				OnSecondTick.Invoke();
			}
		}

		private void onRowClaimClicked(HeroPassRow row, bool advanced)
		{
			if (OnClaimRequested != null)
			{
				OnClaimRequested.Invoke(row.Level, advanced);
			}
		}

		private void onAllReceiveClicked()
		{
			if (OnAllReceiveRequested != null)
			{
				OnAllReceiveRequested.Invoke();
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
