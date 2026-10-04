using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Mail;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 메일 한 통을 여는 팝업. UIManager.ShowMailSlotPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 첨부가 있고 아직 받지 않았으면 수령 버튼만, 그 밖에는 삭제 버튼만 보인다.
	// 받은 메일은 기기 보관함에 남고, 삭제는 보관함에서 지운다(MailArchive).
	// 수령·삭제는 메일함이 넘겨준 공급자로 처리한다 — 표시와 버튼 두 개뿐이라 Presenter 를 두지 않는다.
	public class MailSlotPopup : UIScreen
	{
		[Header("내용")]
		[SerializeField] private TMP_Text _headText;					// HeadText
		[SerializeField] private TMP_Text _bodyText;					// BodyText
		[SerializeField] private RectTransform _itemGrid;				// ItemGrid
		[SerializeField] private ItemSlot _itemSlotPrefab;				// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;		// 등급 색상 SO

		[Header("버튼")]
		[SerializeField] private UIButton _claimButton;				// ClaimButton
		[SerializeField] private UIButton _deleteButton;				// DeleteButton
		[SerializeField] private UIButton _exitButton;					// ExitButton
		[SerializeField] private UIButton _dimButton;					// Dim

		private MailData _mail;
		private IMailProvider _provider;

		// 수령·삭제 요청 중 중복 입력을 막는다.
		private bool _isBusy;

		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			_claimButton.OnClickEvent += onClaimClicked;
			_deleteButton.OnClickEvent += onDeleteClicked;
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;
		}

		private void OnDestroy()
		{
			_claimButton.OnClickEvent -= onClaimClicked;
			_deleteButton.OnClickEvent -= onDeleteClicked;
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;
		}

		// 내용을 채우고 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(MailData mail, IMailProvider provider, CancellationToken ct)
		{
			_mail = mail;
			_provider = provider;

			_headText.text = "보낸 사람 : " + mail.sender
				+ "\n편지 제목 : " + mail.title
				+ "\n받은 날짜 : " + mail.sentAt.ToString(MailSlot.DateFormat);
			_bodyText.text = mail.body;

			CancellationToken slotCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < mail.attachments.Count; i++)
			{
				bindAttachment(mail.attachments[i], slotCt);
			}

			applyButtons();

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 첨부 표시 전용 — OnClicked 를 구독하지 않아 눌러도 반응이 없다.
		// (SetInteractable(false) 는 비활성 틴트가 입혀져 쓰지 않는다)
		private void bindAttachment(RewardPreviewItem item, CancellationToken ct)
		{
			ItemSlot slot = Instantiate(_itemSlotPrefab, _itemGrid);

			if (item.type == RewardType.Currency)
			{
				slot.BindCurrencyAsync(item.currency, item.count, _gradeColors, ct).Forget();
				return;
			}

			if (item.equipment != null)
			{
				slot.BindEquipmentAsync(item.equipment, false, _gradeColors, ct).Forget();
				return;
			}

			slot.BindItemAsync(Table_Item.Get(item.itemId), item.count, _gradeColors, ct).Forget();
		}

		private void applyButtons()
		{
			bool claimable = _mail.HasAttachments && _mail.claimed == false;
			_claimButton.gameObject.SetActive(claimable);
			_deleteButton.gameObject.SetActive(claimable == false);
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

			// 받은 메일은 보관함으로 옮겨져 목록에 남는다 — 버튼을 삭제로 바꾼다.
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

		private void onCloseClicked()
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
