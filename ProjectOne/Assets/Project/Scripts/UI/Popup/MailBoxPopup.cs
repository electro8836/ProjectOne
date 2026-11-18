using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using ProjectOne.Mail;

namespace ProjectOne.UI
{
	// 우편함 팝업(UIPrefab_MailBoxPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Mail/MailBoxPopup.uxml.
	// 목록을 그리고 줄 선택·모두 받기를 Presenter 로 올린다. 목록이 준비되면 Reveal 로 보인다.
	public class MailBoxPopup : ToolkitScreen, IView
	{
		public event Action<MailData> OnMailSelected;
		public event Action OnAllClaimRequested;

		private readonly MailBoxPopupPresenter _presenter = new MailBoxPopupPresenter();
		private readonly List<MailCell> _cells = new List<MailCell>();

		private ScrollView _list;
		private Label _empty;
		private Button _allClaimButton;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));
			_empty = root.Q<Label>("empty");

			_allClaimButton = root.Q<Button>("all-claim");
			_allClaimButton.clicked += onAllClaimClicked;
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

		public void RenderMails(IReadOnlyList<MailData> mails)
		{
			for (int i = 0; i < mails.Count; i++)
			{
				MailCell cell = getOrCreateCell(i);
				cell.SetShown(true);
				cell.Bind(mails[i], MailReadLog.IsRead(mails[i].id));
			}

			for (int i = mails.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			_empty.style.display = mails.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			_allClaimButton.SetEnabled(mails.Count > 0);
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

		private MailCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			MailCell cell = new MailCell(_list.contentContainer);
			cell.OnClicked += onCellClicked;
			_cells.Add(cell);
			return cell;
		}

		private void onCellClicked(MailCell sender)
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

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
