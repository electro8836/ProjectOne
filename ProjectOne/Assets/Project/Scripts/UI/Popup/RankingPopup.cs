using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 전투력 랭킹 팝업의 View(MVP). UIManager.ShowRankingPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 상위 목록(최대 100위)은 Grid 에 슬롯을 깔고, 내 순위는 MyRankSlot 아래 고정 슬롯에 그린다.
	// 목록은 50명씩 온다 — 스크롤이 끝에 가까워지면 OnNeedMore 로 알리고, 받은 줄은 아래에 이어 붙인다.
	// 어떤 슬롯을 누르든 그 플레이어의 정보 팝업을 띄우는 것은 RankingPopupPresenter 의 일이다.
	public class RankingPopup : UIScreen, IView
	{
		// 슬롯을 한 프레임에 이만큼씩 만든다 — 50개를 한꺼번에 만들면 스크롤이 멈칫한다.
		private const int SlotsPerFrame = 10;

		// 보이지 않는 아래쪽 줄이 이만큼 남으면 다음 줄을 요청한다(한 화면 약 11줄).
		private const int NeedMoreRemainRows = 20;

		[Header("목록")]
		[SerializeField] private ScrollRect _scrollRect;		// Frame/ScrollRect
		[SerializeField] private RectTransform _grid;			// Frame/ScrollRect/Viewport/Content/Grid
		[SerializeField] private PlayerRankSlot _slotPrefab;	// UIPrefab_PlayerRankSlot
		[SerializeField] private PlayerRankSlot _myRankSlot;	// Frame/MyRankSlot/UIPrefab_PlayerRankSlot

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;			// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		// 플레이어 슬롯 클릭(목록·내 칸 공통) — 인자는 플레이어 ID.
		public event Action<string> OnPlayerSelected;

		// 목록 끝에 가까워졌다 — 다음 줄이 있는지는 Presenter 가 판단한다. 줄이 붙을 때까지 다시 알리지 않는다.
		public event Action OnNeedMore;

		private readonly RankingPopupPresenter _presenter = new RankingPopupPresenter();
		private readonly List<PlayerRankSlot> _slots = new List<PlayerRankSlot>();

		private CanvasGroup _canvasGroup;
		private UniTaskCompletionSource _tcs;

		// 그려진 줄 수와, 이번 구간에서 이미 요청을 알렸는지.
		private int _shownCount;
		private bool _needMoreRaised;

		private void Awake()
		{
			// 목록이 채워지기 전 빈 칸이 비치지 않도록 숨겨 두고 시작한다.
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			setVisible(false);

			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;
			_myRankSlot.OnClicked += onSlotClicked;
			_scrollRect.onValueChanged.AddListener(onScrollChanged);

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;
			_myRankSlot.OnClicked -= onSlotClicked;
			_scrollRect.onValueChanged.RemoveListener(onScrollChanged);

			for (int i = 0; i < _slots.Count; i++)
			{
				if (_slots[i] != null)
				{
					_slots[i].OnClicked -= onSlotClicked;
				}
			}

			_presenter.Dispose();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			await _presenter.ShowAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		// 처음 그리기 — 내 칸과 받은 목록 전부. 끝까지 그렸으면 true(중간에 닫히면 false).
		public UniTask<bool> RenderRankingAsync(IReadOnlyList<RankEntry> top, in RankEntry mine, CancellationToken ct)
		{
			_myRankSlot.Bind(mine);

			for (int i = top.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			return AppendRankingAsync(top, 0, ct);
		}

		// top 의 startIndex 부터 끝까지를 목록 아래에 붙인다. 스크롤 위치는 건드리지 않는다.
		public async UniTask<bool> AppendRankingAsync(IReadOnlyList<RankEntry> top, int startIndex, CancellationToken ct)
		{
			int built = 0;
			for (int i = startIndex; i < top.Count; i++)
			{
				PlayerRankSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);
				slot.Bind(top[i]);

				built++;
				if (built % SlotsPerFrame == 0 && i < top.Count - 1)
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
			setVisible(true);
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private PlayerRankSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			PlayerRankSlot slot = Instantiate(_slotPrefab, _grid);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);

			return slot;
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		// 아래에 남은(아직 안 보인) 줄 수를 스크롤 위치로 어림한다.
		private void onScrollChanged(Vector2 position)
		{
			if (_needMoreRaised == true || _shownCount == 0)
			{
				return;
			}

			float contentHeight = _scrollRect.content.rect.height;
			float hiddenHeight = contentHeight - _scrollRect.viewport.rect.height;
			if (contentHeight <= 0f || hiddenHeight <= 0f)
			{
				return;
			}

			// verticalNormalizedPosition 은 맨 위 1, 맨 아래 0 이다.
			float rowHeight = contentHeight / _shownCount;
			float remainRows = position.y * hiddenHeight / rowHeight;
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

		private void onSlotClicked(PlayerRankSlot sender, string playerId)
		{
			if (OnPlayerSelected != null)
			{
				OnPlayerSelected.Invoke(playerId);
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
