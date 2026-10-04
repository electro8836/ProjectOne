using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BackEnd;
using ProjectOne.Shared;

namespace ProjectOne.Mail
{
	// 받은 메일 보관함 — 기기 로컬, 계정별 파일.
	//
	// 뒤끝은 받은 우편을 서버 목록에서 지우므로, 무엇을 받았는지 보여 주려고 클라가 따로 남긴다.
	// 재설치·다른 기기에서는 비어 있다(보상은 서버에 이미 반영돼 있다). 삭제는 여기서만 한다.
	public static class MailArchive
	{
		// 최대 보관 수 — 넘치면 가장 오래 전에 보낸 메일부터 지운다.
		private const int MaxCount = 50;

		private const string FilePrefix = "mail_archive_";

		[Serializable]
		private class MailArchiveData
		{
			public List<MailDto> mails = new List<MailDto>();
		}

		private static MailArchiveData _data;
		private static string _loadedUser;

		// 최신순(sentDate 내림차순).
		public static IReadOnlyList<MailDto> All
		{
			get
			{
				ensureLoaded();
				return _data.mails;
			}
		}

		public static bool Contains(int postType, string inDate)
		{
			ensureLoaded();
			return indexOf(postType, inDate) >= 0;
		}

		public static void Add(MailDto mail)
		{
			ensureLoaded();
			if (indexOf(mail.postType, mail.inDate) >= 0)
			{
				return;
			}

			int insertAt = 0;
			while (insertAt < _data.mails.Count && string.CompareOrdinal(_data.mails[insertAt].sentDate, mail.sentDate) > 0)
			{
				insertAt++;
			}

			_data.mails.Insert(insertAt, mail);

			while (_data.mails.Count > MaxCount)
			{
				int last = _data.mails.Count - 1;
				MailReadLog.Clear(ToMailId(_data.mails[last]));
				_data.mails.RemoveAt(last);
			}

			save();
		}

		public static void Remove(int postType, string inDate)
		{
			ensureLoaded();
			int index = indexOf(postType, inDate);
			if (index < 0)
			{
				return;
			}

			MailReadLog.Clear(ToMailId(_data.mails[index]));
			_data.mails.RemoveAt(index);
			save();
		}

		public static void Clear()
		{
			ensureLoaded();
			for (int i = 0; i < _data.mails.Count; i++)
			{
				MailReadLog.Clear(ToMailId(_data.mails[i]));
			}

			_data.mails.Clear();
			save();
		}

		// 화면용 메일 id — 서버 미수령 메일과 같은 규칙이라 수령 전후로 읽음 기록이 이어진다.
		public static string ToMailId(MailDto mail)
		{
			return mail.postType + ":" + mail.inDate;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static int indexOf(int postType, string inDate)
		{
			for (int i = 0; i < _data.mails.Count; i++)
			{
				if (_data.mails[i].postType == postType && _data.mails[i].inDate == inDate)
				{
					return i;
				}
			}

			return -1;
		}

		// 같은 기기에서 계정이 바뀌면 그 계정의 파일을 다시 읽는다.
		private static void ensureLoaded()
		{
			string user = Backend.UserInDate;
			if (_data != null && _loadedUser == user)
			{
				return;
			}

			_loadedUser = user;
			_data = null;

			string path = getPath();
			if (File.Exists(path) == true)
			{
				_data = JsonUtility.FromJson<MailArchiveData>(File.ReadAllText(path));
			}

			if (_data == null)
			{
				_data = new MailArchiveData();
			}
		}

		private static void save()
		{
			File.WriteAllText(getPath(), JsonUtility.ToJson(_data));
		}

		// UserInDate 는 날짜 형식이라 ':' 가 섞인다 — 파일 이름에 못 쓰는 문자를 바꾼다.
		private static string getPath()
		{
			string user = string.IsNullOrEmpty(_loadedUser) ? "guest" : _loadedUser.Replace(':', '-');
			return Path.Combine(Application.persistentDataPath, FilePrefix + user + ".json");
		}
	}
}
