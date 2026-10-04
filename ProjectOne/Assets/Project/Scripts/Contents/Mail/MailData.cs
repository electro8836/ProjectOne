using System;
using System.Collections.Generic;
using ProjectOne.Reward;

namespace ProjectOne.Mail
{
	// 메일 한 통. 구매·보상·공지가 같은 모양으로 들어온다.
	//
	// 첨부는 RewardPreviewItem 으로 담는다 — 수령 전에는 "무엇이 들어있는지" 만 있고,
	// 실제 지급 결과(GrantedReward)는 수령 시점에 공급자가 만든다.
	public class MailData
	{
		public string id;
		public string title;
		public string sender;
		public string body;
		public DateTime sentAt;
		public readonly List<RewardPreviewItem> attachments = new List<RewardPreviewItem>();

		// 서버에서 받았는지(보관함에 있는지). 메시지만 있는 메일은 읽으면 받는다.
		public bool claimed;

		public bool HasAttachments
		{
			get { return attachments.Count > 0; }
		}
	}
}
