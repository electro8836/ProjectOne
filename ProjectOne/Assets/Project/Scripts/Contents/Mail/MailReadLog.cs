using UnityEngine;

namespace ProjectOne.Mail
{
	// 메일 열람 기록. 서버가 아니라 기기 로컬(PlayerPrefs)에 남긴다 — 수령·삭제와는 별개다.
	public static class MailReadLog
	{
		private const string KeyPrefix = "MailRead_";

		public static bool IsRead(string mailId)
		{
			return PlayerPrefs.GetInt(KeyPrefix + mailId, 0) == 1;
		}

		public static void MarkRead(string mailId)
		{
			if (IsRead(mailId) == true)
			{
				return;
			}

			PlayerPrefs.SetInt(KeyPrefix + mailId, 1);
			PlayerPrefs.Save();
		}

		// 열람 기록을 지운다. 여러 통을 지울 수 있어 저장(PlayerPrefs.Save)은 호출자가 한다.
		public static void Clear(string mailId)
		{
			PlayerPrefs.DeleteKey(KeyPrefix + mailId);
		}
	}
}
