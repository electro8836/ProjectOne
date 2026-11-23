using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Mail;
using ProjectOne.Resources;
using ProjectOne.Reward;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 우편함 목록 줄 1개 (UI Toolkit). 카드에 상태 아이콘(첨부면 선물, 아니면 편지)·제목·보낸 사람과 날짜, 오른쪽에 첫 첨부 칸을 그린다.
	// 안 읽은 우편은 보라 카드 + 빨간 점, 첨부가 둘 이상이면 "+N", 받은 첨부는 칸을 흐리게 + 체크. 스타일은 UIToolkit/Popups/Mail/Mail.uss.
	// 줄 어디를 눌러도 우편 한 통 팝업이 열린다 — 첨부 칸은 클릭을 받지 않는다.
	public sealed class MailCell
	{
		public const string DateFormat = "yyyy.MM.dd HH:mm";

		private const string GiftIconName = "ItemIcon_Gift";
		private const string MailIconName = "ItemIcon_Mail";
		private const string MetaSeparator = "  ·  ";

		private readonly VisualElement _root;
		private readonly VisualElement _icon;
		private readonly VisualElement _dot;
		private readonly Label _title;
		private readonly Label _meta;
		private readonly VisualElement _reward;
		private readonly ItemCellView _rewardCell;
		private readonly VisualElement _check;
		private readonly Label _more;

		private MailData _mail;

		public event Action<MailCell> OnClicked;

		public MailData Mail
		{
			get { return _mail; }
		}

		public MailCell(VisualElement parent, ItemGradeColorTable colors, ToolkitIconLoader icons)
		{
			_root = new VisualElement();
			_root.AddToClassList("mail-row");
			_root.RegisterCallback<ClickEvent>(onClicked);
			_root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			VisualElement state = new VisualElement();
			state.AddToClassList("mail-row__state");
			state.pickingMode = PickingMode.Ignore;
			_icon = new VisualElement();
			_icon.AddToClassList("mail-row__icon");
			_icon.pickingMode = PickingMode.Ignore;
			_dot = new VisualElement();
			_dot.AddToClassList("mail-row__dot");
			_dot.pickingMode = PickingMode.Ignore;
			state.Add(_icon);
			state.Add(_dot);
			_root.Add(state);

			VisualElement text = new VisualElement();
			text.AddToClassList("mail-row__text");
			text.pickingMode = PickingMode.Ignore;
			_title = addLabel(text, "mail-row__title");
			_meta = addLabel(text, "mail-row__meta");
			_root.Add(text);

			_reward = new VisualElement();
			_reward.AddToClassList("mail-row__reward");
			_rewardCell = new ItemCellView(_reward, colors, icons);
			_check = new VisualElement();
			_check.AddToClassList("mail-row__check");
			_reward.Add(_check);
			_more = addLabel(_reward, "mail-row__more");
			ignorePicking(_reward);
			_root.Add(_reward);

			parent.Add(_root);
		}

		public void Bind(MailData mail, bool read, CancellationToken ct)
		{
			_mail = mail;
			_title.text = mail.title;
			_meta.text = mail.sender + MetaSeparator + mail.sentAt.ToString(DateFormat);

			Sprite icon = AtlasManager.Instance.Get(mail.HasAttachments ? GiftIconName : MailIconName);
			_icon.style.backgroundImage = (icon != null) ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);

			_root.EnableInClassList("mail-row--claimed", mail.HasAttachments && mail.claimed);
			SetRead(read);
			bindReward(mail, ct);
		}

		// 열어 본 우편은 회색 카드, 안 읽은 우편은 보라 카드 + 빨간 점.
		public void SetRead(bool read)
		{
			_root.EnableInClassList("mail-row--read", read);
			_dot.style.display = read ? DisplayStyle.None : DisplayStyle.Flex;
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 첫 첨부만 칸으로 보이고, 나머지는 "+N" 으로 센다.
		private void bindReward(MailData mail, CancellationToken ct)
		{
			if (mail.attachments.Count == 0)
			{
				_reward.style.display = DisplayStyle.None;
				return;
			}

			_reward.style.display = DisplayStyle.Flex;
			_check.style.display = mail.claimed ? DisplayStyle.Flex : DisplayStyle.None;

			int extra = mail.attachments.Count - 1;
			_more.style.display = extra > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			_more.text = "+" + extra;

			RewardPreviewItem item = mail.attachments[0];
			UniTask task;
			if (item.type == RewardType.Currency)
			{
				task = _rewardCell.BindCurrencyAsync(item.currency, item.count, ct);
				_rewardCell.SetCountText(NumberFormat.ToShort(item.count));	// 칸이 작아 큰 수는 999K 처럼 줄인다
			}
			else if (item.equipment != null)
			{
				task = _rewardCell.BindEquipmentAsync(item.equipment, false, ct);
			}
			else
			{
				task = _rewardCell.BindItemAsync(Table_Item.Get(item.itemId), item.count, ct);
				if (item.count > 1)
				{
					_rewardCell.SetCountText(NumberFormat.ToShort(item.count));
				}
			}

			// 틀은 흰색 — 등급색은 Bind 의 동기 구간에서 인라인으로 칠해지므로 그 뒤에 덮어쓴다
			_rewardCell.Root.style.unityBackgroundImageTintColor = Color.white;
			task.Forget();
		}

		private static void ignorePicking(VisualElement element)
		{
			element.pickingMode = PickingMode.Ignore;
			for (int i = 0; i < element.hierarchy.childCount; i++)
			{
				ignorePicking(element.hierarchy[i]);
			}
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
