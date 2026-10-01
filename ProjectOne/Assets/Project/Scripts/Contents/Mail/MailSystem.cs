using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Mail
{
	// 메일 공급자의 단일 접근점. 메일함 팝업과 배지(메인 HUD·메뉴 팝업)가 같은 목록을 본다.
	public static class MailSystem
	{
		// [임시] 뒤끝 우편이 생기면 뒤끝 구현체로 교체한다.
		public static IMailProvider Provider = new DummyMailProvider();

		// 삭제하지 않은 메일 중 아직 열어 보지 않은 것이 있는지. 수령 여부와는 무관하다.
		public static bool HasUnread(IReadOnlyList<MailData> mails)
		{
			for (int i = 0; i < mails.Count; i++)
			{
				if (MailReadLog.IsRead(mails[i].id) == false)
				{
					return true;
				}
			}

			return false;
		}

		// 공급자에서 목록을 받아 판정한다. 취소되면 false.
		public static async UniTask<bool> HasUnreadAsync(CancellationToken ct)
		{
			(bool cancelled, IReadOnlyList<MailData> mails) = await Provider.GetMailsAsync(ct).SuppressCancellationThrow();
			if (cancelled == true || mails == null)
			{
				return false;
			}

			return HasUnread(mails);
		}
	}
}
