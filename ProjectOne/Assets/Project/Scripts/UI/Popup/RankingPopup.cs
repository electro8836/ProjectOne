using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 전투력 랭킹 팝업의 View(MVP). UIManager.ShowRankingPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 상위 목록(최대 100위)은 Grid 에 슬롯을 깔고, 내 순위는 MyRankSlot 아래 고정 슬롯에 그린다.
	// 어떤 슬롯을 누르든 그 플레이어의 정보 팝업을 띄우는 것은 RankingPopupPresenter 의 일이다.
	public class RankingPopup : UIScreen, IView
	{
		[Header("목록")]
		[SerializeField] private RectTransform _grid;			// Frame/ScrollRect/Viewport/Content/Grid
		[SerializeField] private PlayerRankSlot _slotPrefab;	// UIPrefab_PlayerRankSlot
		[SerializeField] private PlayerRankSlot _myRankSlot;	// Frame/MyRankSlot/UIPrefab_PlayerRankSlot

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;			// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		// 플레이어 슬롯 클릭(목록·내 칸 공통) — 인자는 플레이어 ID.
		public event Action<string> OnPlayerSelected;

		private readonly RankingPopupPresenter _presenter = new RankingPopupPresenter();
		private readonly List<PlayerRankSlot> _slots = new List<PlayerRankSlot>();

		private CanvasGroup _canvasGroup;
		private UniTaskCompletionSource _tcs;

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

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;
			_myRankSlot.OnClicked -= onSlotClicked;

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

		public void RenderRanking(IReadOnlyList<RankEntry> top, in RankEntry mine)
		{
			for (int i = 0; i < top.Count; i++)
			{
				PlayerRankSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);
				slot.Bind(top[i]);
			}

			for (int i = top.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			_myRankSlot.Bind(mine);
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
