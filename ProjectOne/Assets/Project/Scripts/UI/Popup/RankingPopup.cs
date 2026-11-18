using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 랭킹 팝업(UIPrefab_RankingPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Ranking/RankingPopup.uxml.
	// 상위 목록과 내 순위를 보여 준다. 줄을 누르면 그 플레이어 정보 팝업을 띄우도록 Presenter 로 올리고,
	// 남은 줄이 적어지도록 내리면 다음 페이지를 요청한다.
	public class RankingPopup : ToolkitScreen, IView
	{
		// 한 프레임에 만드는 줄 수 — 수백 줄을 한 번에 만들면 프레임이 튄다.
		private const int RowsPerFrame = 10;

		// 아래에 남은 줄이 이만큼 이하로 보이면 다음 페이지를 요청한다.
		private const int NeedMoreRemainRows = 20;

		public event Action<string> OnPlayerSelected;
		public event Action OnNeedMore;

		private readonly RankingPopupPresenter _presenter = new RankingPopupPresenter();
		private readonly List<RankCell> _cells = new List<RankCell>();

		private ScrollView _list;
		private RankCell _mine;
		private UniTaskCompletionSource _tcs;
		private int _shownCount;
		private bool _needMoreRaised;

		protected override void OnBind(VisualElement root)
		{
			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));
			_list.verticalScroller.valueChanged += onScrollChanged;

			_mine = new RankCell(root.Q("mine"));
			_mine.OnClicked += onCellClicked;

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

		public async UniTask ShowAsync(CancellationToken ct)
		{
			await _presenter.ShowAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public UniTask<bool> RenderRankingAsync(IReadOnlyList<RankEntry> top, in RankEntry mine, CancellationToken ct)
		{
			_mine.Bind(mine);

			for (int i = top.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			return AppendRankingAsync(top, 0, ct);
		}

		// startIndex 부터 줄을 만든다. 취소되면 false.
		public async UniTask<bool> AppendRankingAsync(IReadOnlyList<RankEntry> top, int startIndex, CancellationToken ct)
		{
			int built = 0;
			for (int i = startIndex; i < top.Count; i++)
			{
				RankCell cell = getOrCreateCell(i);
				cell.SetShown(true);
				cell.Bind(top[i]);
				built++;

				if (built % RowsPerFrame == 0 && i < top.Count - 1)
				{
					bool cancelled = await UniTask.Yield(PlayerLoopTiming.Update, ct).SuppressCancellationThrow();
					if (cancelled == true)
					{
						return false;
					}
				}
			}

			_shownCount = top.Count;
			_needMoreRaised = false;
			return true;
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

		private RankCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			RankCell cell = new RankCell(_list.contentContainer);
			cell.OnClicked += onCellClicked;
			_cells.Add(cell);
			return cell;
		}

		// 아래에 남은 줄 수 = (내용 높이 - 보이는 아래 끝) / 줄 높이.
		private void onScrollChanged(float offset)
		{
			if (_needMoreRaised == true || _shownCount == 0)
			{
				return;
			}

			float contentHeight = _list.contentContainer.layout.height;
			float viewportHeight = _list.contentViewport.layout.height;
			if (float.IsNaN(contentHeight) == true || contentHeight <= viewportHeight)
			{
				return;
			}

			float rowHeight = contentHeight / _shownCount;
			float remainRows = (contentHeight - (offset + viewportHeight)) / rowHeight;
			if (remainRows > NeedMoreRemainRows)
			{
				return;
			}

			_needMoreRaised = true;
			if (OnNeedMore != null)
			{
				OnNeedMore.Invoke();
			}
		}

		private void onCellClicked(RankCell sender, string playerId)
		{
			if (OnPlayerSelected != null)
			{
				OnPlayerSelected.Invoke(playerId);
			}
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
