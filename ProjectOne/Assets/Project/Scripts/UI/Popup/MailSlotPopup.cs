using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Mail;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 메일 한 통 팝업(UIPrefab_MailSlotPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Mail/MailSlotPopup.uxml.
	// 머리말·본문·첨부를 보여 주고, 받을 첨부가 있으면 받기, 아니면 삭제 버튼을 둔다.
	public class MailSlotPopup : ToolkitScreen
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private Label _head;
		private Label _body;
		private VisualElement _attachments;
		private Button _claimButton;
		private Button _deleteButton;

		private MailData _mail;
		private IMailProvider _provider;
		private bool _isBusy;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_head = root.Q<Label>("head");
			_body = root.Q<Label>("body");
			_attachments = root.Q("attachments");

			_claimButton = root.Q<Button>("claim");
			_claimButton.clicked += onClaimClicked;
			_deleteButton = root.Q<Button>("delete");
			_deleteButton.clicked += onDeleteClicked;

			root.Q<Button>("close").clicked += close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
		}

		protected override void OnDestroy()
		{
			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public async UniTask ShowAsync(MailData mail, IMailProvider provider, CancellationToken ct)
		{
			_mail = mail;
			_provider = provider;

			_head.text = "보낸 사람 : " + mail.sender
				+ "\n편지 제목 : " + mail.title
				+ "\n받은 날짜 : " + mail.sentAt.ToString(MailCell.DateFormat);
			_body.text = mail.body;

			CancellationToken cellCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < mail.attachments.Count; i++)
			{
				bindAttachment(mail.attachments[i], cellCt);
			}

			_attachments.style.display = mail.attachments.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			applyButtons();

			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		private void bindAttachment(RewardPreviewItem item, CancellationToken ct)
		{
			ItemCellView cell = new ItemCellView(_attachments, _gradeColors, _icons);

			if (item.type == RewardType.Currency)
			{
				cell.BindCurrencyAsync(item.currency, item.count, ct).Forget();
				return;
			}

			if (item.equipment != null)
			{
				cell.BindEquipmentAsync(item.equipment, false, ct).Forget();
				return;
			}

			cell.BindItemAsync(Table_Item.Get(item.itemId), item.count, ct).Forget();
		}

		// 받을 첨부가 남았으면 받기, 아니면(첨부 없음·이미 받음) 삭제.
		private void applyButtons()
		{
			bool claimable = _mail.HasAttachments && _mail.claimed == false;
			_claimButton.style.display = claimable ? DisplayStyle.Flex : DisplayStyle.None;
			_deleteButton.style.display = claimable ? DisplayStyle.None : DisplayStyle.Flex;
		}

		private void onClaimClicked()
		{
			claimAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		private async UniTaskVoid claimAsync(CancellationToken ct)
		{
			if (_isBusy == true)
			{
				return;
			}

			_isBusy = true;
			(bool cancelled, List<GrantedReward> rewards) = await _provider.ClaimAsync(_mail.id, ct).SuppressCancellationThrow();
			_isBusy = false;
			if (cancelled == true)
			{
				return;
			}

			applyButtons();
			await UIManager.Instance.ShowRewardPopupAsync(rewards, ct).SuppressCancellationThrow();
		}

		private void onDeleteClicked()
		{
			deleteAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		private async UniTaskVoid deleteAsync(CancellationToken ct)
		{
			if (_isBusy == true)
			{
				return;
			}

			_isBusy = true;
			bool cancelled = await _provider.DeleteAsync(_mail.id, ct).SuppressCancellationThrow();
			_isBusy = false;
			if (cancelled == true)
			{
				return;
			}

			close();
		}

		private void onDimClicked(ClickEvent evt)
		{
			close();
		}

		private void close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}
	}
}
