using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Mail;

namespace ProjectOne.UI
{
	// 메일함 팝업의 View(MVP). UIManager.ShowMailBoxPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 삭제하지 않은 메일을 Grid 에 깔기만 한다 — 메일 열기와 모두 수령은 MailBoxPopupPresenter 의 일이다.
	public class MailBoxPopup : UIScreen, IView
	{
		[Header("목록")]
		[SerializeField] private RectTransform _grid;			// Frame/ScrollRect/Viewport/Grid
		[SerializeField] private MailSlot _slotPrefab;			// UIPrefab_MailSlot

		[Header("버튼")]
		[SerializeField] private UIButton _allClaimButton;		// Frame/AllClaimButton
		[SerializeField] private UIButton _exitButton;			// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		public event Action<MailData> OnMailSelected;
		public event Action OnAllClaimRequested;

		private readonly MailBoxPopupPresenter _presenter = new MailBoxPopupPresenter();
		private readonly List<MailSlot> _slots = new List<MailSlot>();

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

			_allClaimButton.OnClickEvent += onAllClaimClicked;
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_allClaimButton.OnClickEvent -= onAllClaimClicked;
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;

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

		public void RenderMails(IReadOnlyList<MailData> mails)
		{
			for (int i = 0; i < mails.Count; i++)
			{
				MailSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);
				slot.Bind(mails[i], MailReadLog.IsRead(mails[i].id));
			}

			for (int i = mails.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			_allClaimButton.interactable = mails.Count > 0;
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

		private MailSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			MailSlot slot = Instantiate(_slotPrefab, _grid);
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

		private void onSlotClicked(MailSlot sender)
		{
			if (OnMailSelected != null)
			{
				OnMailSelected.Invoke(sender.Mail);
			}
		}

		private void onAllClaimClicked()
		{
			if (OnAllClaimRequested != null)
			{
				OnAllClaimRequested.Invoke();
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
