using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using EDT;

namespace ProjectOne.UI
{
	// 출석 팝업(UIPrefab_DailyBonusPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/DailyBonus/DailyBonusPopup.uxml.
	// 주간(7일)·월간 탭을 오가며 칸을 그린다. 남은 시간 문구는 Presenter 가 매초(OnSecondTick) 갱신한다.
	public class DailyBonusPopup : ToolkitScreen, IView
	{
		private const int TAB_WEEK = 0;
		private const int TAB_MONTH = 1;
		private const long TickIntervalMs = 1000;

		private readonly DailyBonusPopupPresenter _presenter = new DailyBonusPopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<DailyBonusCell> _cells = new List<DailyBonusCell>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();
		private readonly Button[] _tabs = new Button[2];

		private Label _title;
		private Label _time;
		private VisualElement _grid;
		private IVisualElementScheduledItem _tick;
		private UniTaskCompletionSource _tcs;

		public event Action OnSecondTick;
		public event Action<DailyBonusType> OnTabSelected;
		public event Action<int> OnClaimRequested;

		protected override void OnBind(VisualElement root)
		{
			_title = root.Q<Label>("title");
			_time = root.Q<Label>("time");
			_grid = root.Q("grid");

			for (int i = 0; i < _tabs.Length; i++)
			{
				Button tab = root.Q<Button>("tab-" + i);
				tab.userData = i;
				tab.RegisterCallback<ClickEvent>(onTabClicked);
				_tabs[i] = tab;
			}

			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
			root.Q<Button>("close").clicked += Close;

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

		public async UniTask RenderAsync(DailyBonusType type, IReadOnlyList<DailyBonusSlotData> data, CancellationToken ct)
		{
			bool isMonth = (type == DailyBonusType.Month);
			_title.text = isMonth ? "월간 출석" : "주간 출석";
			_grid.EnableInClassList("daily-grid--month", isMonth);

			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				DailyBonusCell cell = getOrCreateCell(i);
				cell.SetShown(true);
				_bindTasks.Add(cell.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		public void SetTimeText(string text)
		{
			_time.text = text;
		}

		// 주간 탭만 선택(OnTabSelected 를 발행하지 않음 — 초기 표시용).
		public void SelectWeekTab()
		{
			selectTab(TAB_WEEK);
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

		private DailyBonusCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			DailyBonusCell cell = new DailyBonusCell(_grid, _icons, GetDestroyToken());
			cell.OnClaimClicked += onCellClaimClicked;
			_cells.Add(cell);
			return cell;
		}

		private void selectTab(int index)
		{
			for (int i = 0; i < _tabs.Length; i++)
			{
				_tabs[i].EnableInClassList("tab--selected", i == index);
			}
		}

		private void onTick()
		{
			if (OnSecondTick != null)
			{
				OnSecondTick.Invoke();
			}
		}

		private void onTabClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			selectTab(index);

			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke(index == TAB_MONTH ? DailyBonusType.Month : DailyBonusType.Week);
			}
		}

		private void onCellClaimClicked(DailyBonusCell cell)
		{
			if (OnClaimRequested != null)
			{
				OnClaimRequested.Invoke(cell.DayCount);
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
