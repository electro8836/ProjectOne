namespace ProjectOne.Shared
{
	// 우편 패킷 — 뒤끝 우편(관리자·랭킹)을 서버가 읽고 수령·지급한다.
	//
	// 첨부는 콘솔 차트 MailItem 의 행(RewardType, TargetID) + 발송 시 입력한 수량이다.

	// 우편 종류 — 뒤끝 PostType 중 이 게임이 받는 것만. 서버가 BackEnd.PostType 으로 바꾼다.
	public static class MailPostType
	{
		public const int Admin = 0;
		public const int Rank = 1;
	}

	[System.Serializable]
	public class MailAttachmentDto
	{
		public int rewardType;		// RewardType (Item / Currency)
		public int targetId;		// 아이템 ID 또는 재화 ID
		public int count;
	}

	[System.Serializable]
	public class MailDto
	{
		public int postType;		// MailPostType
		public string inDate;		// 우편 식별자 — 수령 요청에 그대로 쓴다
		public string title;
		public string content;
		public string author;		// 관리자 우편은 발송인, 랭킹 우편은 랭킹 종류
		public string sentDate;		// ISO 8601 (UTC)
		public string expirationDate;
		public MailAttachmentDto[] items;
	}

	[System.Serializable]
	public class MailListRequest
	{
	}

	// 관리자·랭킹 우편을 합쳐 최신순으로 내려준다.
	[System.Serializable]
	public class MailListResponse : ServerResponse
	{
		public MailDto[] mails;
	}

	// inDate 가 비어 있으면 그 종류의 우편을 모두 받는다.
	[System.Serializable]
	public class MailReceiveRequest
	{
		public int postType;
		public string inDate;
	}

	[System.Serializable]
	public class MailReceiveResponse : ServerResponse
	{
		// 스택 아이템·재화·수집품. 장비는 등급·품질이 고정되지 않아 MailItem 차트에 넣지 않는다.
		public GrantedRewardDto[] rewards;
	}
}
