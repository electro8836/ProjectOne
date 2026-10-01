using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Mail;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 메일함 목록의 한 줄(UIPrefab_MailSlot). 누르면 그 메일을 여는 것은 소유 화면의 일이다.
	public class MailSlot : MonoBehaviour
	{
		public const string DateFormat = "yyyy.MM.dd HH:mm";

		private const string GiftIconName = "ItemIcon_Gift";
		private const string MailIconName = "ItemIcon_Mail";

		[SerializeField] private UIButton _button;			// 루트 UIButton
		[SerializeField] private TMP_Text _titleText;		// TitleText
		[SerializeField] private TMP_Text _senderText;		// SenerText
		[SerializeField] private TMP_Text _dateText;		// DataText
		[SerializeField] private Image _stateIcon;			// State/Icon
		[SerializeField] private GameObject _check;			// State/Check — 첨부 수령 완료
		[SerializeField] private GameObject _dim;			// Dim — 열어 본 메일

		public event Action<MailSlot> OnClicked;

		private MailData _mail;

		public MailData Mail
		{
			get { return _mail; }
		}

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;
		}

		public void Bind(MailData mail, bool read)
		{
			_mail = mail;

			_titleText.text = mail.title;
			_senderText.text = mail.sender;
			_dateText.text = mail.sentAt.ToString(DateFormat);

			_stateIcon.sprite = AtlasManager.Instance.Get(mail.HasAttachments ? GiftIconName : MailIconName);
			_check.SetActive(mail.HasAttachments && mail.claimed);

			SetRead(read);
		}

		public void SetRead(bool read)
		{
			_dim.SetActive(read);
		}

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this);
			}
		}
	}
}
