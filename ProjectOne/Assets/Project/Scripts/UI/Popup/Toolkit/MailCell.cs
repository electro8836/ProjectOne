using System;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Mail;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 우편함 목록 줄 1개 (UI Toolkit). 제목·보낸 사람·날짜와 상태 아이콘(첨부면 선물, 아니면 편지)을 그린다.
	// 첨부를 받았으면 체크, 열어 본 메일은 흐리게 보인다. 스타일은 UIToolkit/Popups/Mail/Mail.uss.
	public sealed class MailCell
	{
		public const string DateFormat = "yyyy.MM.dd HH:mm";

		private const string GiftIconName = "ItemIcon_Gift";
		private const string MailIconName = "ItemIcon_Mail";

		private readonly VisualElement _root;
		private readonly VisualElement _icon;
		private readonly VisualElement _check;
		private readonly Label _title;
		private readonly Label _sender;
		private readonly Label _date;

		private MailData _mail;

		public event Action<MailCell> OnClicked;

		public MailData Mail
		{
			get { return _mail; }
		}

		public MailCell(VisualElement parent)
		{
			_root = new VisualElement();
			_root.AddToClassList("mail-row");
			_root.RegisterCallback<ClickEvent>(onClicked);
			_root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			VisualElement state = new VisualElement();
			state.AddToClassList("mail-row__state");
			state.pickingMode = PickingMode.Ignore;
			_icon = new VisualElement();
			_icon.AddToClassList("icon");
			_icon.pickingMode = PickingMode.Ignore;
			_check = new VisualElement();
			_check.AddToClassList("mail-row__check");
			_check.pickingMode = PickingMode.Ignore;
			state.Add(_icon);
			state.Add(_check);
			_root.Add(state);

			VisualElement text = new VisualElement();
			text.AddToClassList("mail-row__text");
			text.pickingMode = PickingMode.Ignore;
			_title = addLabel(text, "mail-row__title");
			_sender = addLabel(text, "mail-row__sender");
			_date = addLabel(text, "mail-row__date");
			_root.Add(text);

			parent.Add(_root);
		}

		public void Bind(MailData mail, bool read)
		{
			_mail = mail;
			_title.text = mail.title;
			_sender.text = mail.sender;
			_date.text = mail.sentAt.ToString(DateFormat);

			Sprite icon = AtlasManager.Instance.Get(mail.HasAttachments ? GiftIconName : MailIconName);
			_icon.style.backgroundImage = (icon != null) ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
			_check.style.display = (mail.HasAttachments && mail.claimed) ? DisplayStyle.Flex : DisplayStyle.None;

			SetRead(read);
		}

		// 열어 본 메일은 흐리게.
		public void SetRead(bool read)
		{
			_root.EnableInClassList("mail-row--read", read);
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private static Label addLabel(VisualElement parent, string className)
		{
			Label label = new Label();
			label.AddToClassList(className);
			label.pickingMode = PickingMode.Ignore;
			parent.Add(label);
			return label;
		}

		private void onClicked(ClickEvent evt)
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this);
			}
		}
	}
}
