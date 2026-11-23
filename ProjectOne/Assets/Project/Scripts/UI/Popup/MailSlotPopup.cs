using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Mail;
using ProjectOne.Reward;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 메일 한 통 팝업(UIPrefab_MailSlotPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Mail/MailSlotPopup.uxml.
	// 제목·보낸 사람과 날짜·본문(고정 높이 판, 넘치면 스크롤)·첨부를 보여 주고, 받을 첨부가 있으면 받기, 아니면 삭제 버튼을 둔다.
	// 첨부 칸은 흰 틀 + 수량 축약(999K), 받은 뒤에는 칸을 흐리게 + 체크 — 우편함 목록 줄과 같은 표시.
	public class MailSlotPopup : ToolkitScreen
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private readonly List<VisualElement> _attachSlots = new List<VisualElement>(4);

		private Label _title;
		private Label _meta;
		private Label _body;
		private ScrollView _bodyScroll;
		private VisualElement _attachGroup;
		private VisualElement _attachments;
		private Button _claimButton;
		private Button _deleteButton;

		private MailData _mail;
		private IMailProvider _provider;
		private bool _isBusy;
		private UniTaskCompletionSource _tcs;

		protected override void OnBind(VisualElement root)
		{
			_title = root.Q<Label>("title");
			_meta = root.Q<Label>("meta");
			_body = root.Q<Label>("body");
			_bodyScroll = root.Q<ScrollView>("body-scroll");
			_bodyScroll.AddManipulator(new ScrollDragManipulator(_bodyScroll));
			_attachGroup = root.Q("attach-group");
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

			_title.text = mail.title;
			_meta.text = mail.sender + "  ·  " + mail.sentAt.ToString(MailCell.DateFormat);
			_body.text = mail.body;
			_bodyScroll.scrollOffset = Vector2.zero;

			CancellationToken cellCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < mail.attachments.Count; i++)
			{
				bindAttachment(mail.attachments[i], cellCt);
			}

			_attachGroup.style.display = mail.attachments.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			applyButtons();

			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		private void bindAttachment(RewardPreviewItem item, CancellationToken ct)
		{
			// 칸 + 받음 체크를 한 묶음으로 — 체크는 칸 위에 얹는다
			VisualElement slot = new VisualElement();
			slot.AddToClassList("mail-attach");
			_attachments.Add(slot);
			ItemCellView cell = new ItemCellView(slot, _gradeColors, _icons);
			VisualElement check = new VisualElement();
			check.AddToClassList("mail-attach__check");
			check.pickingMode = PickingMode.Ignore;
			slot.Add(check);
			_attachSlots.Add(slot);

			UniTask task;
			if (item.type == RewardType.Currency)
			{
				task = cell.BindCurrencyAsync(item.currency, item.count, ct);
				cell.SetCountText(NumberFormat.ToShort(item.count));
			}
			else if (item.equipment != null)
			{
				task = cell.BindEquipmentAsync(item.equipment, false, ct);
			}
			else
			{
				task = cell.BindItemAsync(Table_Item.Get(item.itemId), item.count, ct);
				if (item.count > 1)
				{
					cell.SetCountText(NumberFormat.ToShort(item.count));
				}
			}

			// 틀은 흰색 — 등급색은 Bind 의 동기 구간에서 인라인으로 칠해지므로 그 뒤에 덮어쓴다
			cell.Root.style.unityBackgroundImageTintColor = Color.white;
			task.Forget();
		}

		// 받을 첨부가 남았으면 받기, 아니면(첨부 없음·이미 받음) 삭제.
		private void applyButtons()
		{
			bool claimable = _mail.HasAttachments && _mail.claimed == false;
			_claimButton.style.display = claimable ? DisplayStyle.Flex : DisplayStyle.None;
			_deleteButton.style.display = claimable ? DisplayStyle.None : DisplayStyle.Flex;

			for (int i = 0; i < _attachSlots.Count; i++)
			{
				_attachSlots[i].EnableInClassList("mail-attach--claimed", _mail.claimed);
			}
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
