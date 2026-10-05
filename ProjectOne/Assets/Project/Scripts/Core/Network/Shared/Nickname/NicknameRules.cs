using System;
using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 닉네임 검사 결과. None 이면 통과.
	public enum NicknameError
	{
		None = 0,
		TooShort,
		TooLong,
		InvalidChar,
		StartsWithDigit,
		BanWord,	// 금칙어 — 이 프로젝트에서 막는 단어(운영 사칭 등)
		BadWord		// 비속어 — 욕설 목록
	}

	// 닉네임 규칙 — 클라(입력 사전 검사)와 서버(최종 검증)가 같은 코드를 쓴다.
	//
	// 길이는 byte 로 센다: 영문·숫자 1, 한글·일본어 2. 3~16byte 라 한글은 2~8자, 영숫자는 3~16자다.
	// 공백·특수문자·한글 자모 단독은 쓸 수 없고, 첫 글자는 숫자일 수 없다.
	public static class NicknameRules
	{
		public const int MinBytes = 3;
		public const int MaxBytes = 16;

		// 처음 FreeChangeCount 번은 무료, 그 뒤로는 고정 비용.
		public const int FreeChangeCount = 1;
		public const int ChangeCost = 5000;
		public const EDT.Currency CostCurrency = EDT.Currency.Dia;

		// 서버 거절 사유 — 클라가 안내 문구를 고르는 데 쓴다.
		public const string ErrorInvalid = "nickname invalid";
		public const string ErrorSame = "nickname same";
		public const string ErrorDuplicated = "nickname duplicated";
		public const string ErrorNotEnoughCurrency = "not enough currency";

		public static int GetByteCount(string nickname)
		{
			if (string.IsNullOrEmpty(nickname) == true)
			{
				return 0;
			}

			int bytes = 0;
			for (int i = 0; i < nickname.Length; i++)
			{
				bytes += getCharBytes(nickname[i]);
			}

			return bytes;
		}

		// MaxBytes 를 넘기는 글자부터 뒤를 잘라 낸다.
		public static string TrimToMaxBytes(string nickname)
		{
			if (string.IsNullOrEmpty(nickname) == true)
			{
				return string.Empty;
			}

			int bytes = 0;
			for (int i = 0; i < nickname.Length; i++)
			{
				bytes += getCharBytes(nickname[i]);
				if (bytes > MaxBytes)
				{
					return nickname.Substring(0, i);
				}
			}

			return nickname;
		}

		// 금칙어·비속어 검사는 테이블(BanWord, BadWord)이 로드돼 있어야 한다.
		public static NicknameError Validate(string nickname)
		{
			int bytes = GetByteCount(nickname);
			if (bytes < MinBytes)
			{
				return NicknameError.TooShort;
			}

			if (bytes > MaxBytes)
			{
				return NicknameError.TooLong;
			}

			for (int i = 0; i < nickname.Length; i++)
			{
				if (isAllowedChar(nickname[i]) == false)
				{
					return NicknameError.InvalidChar;
				}
			}

			if (isDigit(nickname[0]) == true)
			{
				return NicknameError.StartsWithDigit;
			}

			if (containsBanWord(nickname) == true)
			{
				return NicknameError.BanWord;
			}

			if (containsBadWord(nickname) == true)
			{
				return NicknameError.BadWord;
			}

			return NicknameError.None;
		}

		// changeCount = 지금까지 바꾼 횟수. 무료면 0.
		public static int GetCost(int changeCount)
		{
			return (changeCount < FreeChangeCount) ? 0 : ChangeCost;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static int getCharBytes(char c)
		{
			return (c < 0x80) ? 1 : 2;
		}

		private static bool isDigit(char c)
		{
			return c >= '0' && c <= '9';
		}

		private static bool isAllowedChar(char c)
		{
			if (isDigit(c) == true || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
			{
				return true;
			}

			// 한글 완성형 — 자모 단독(ㄱ, ㅏ)은 범위 밖이다.
			if (c >= 0xAC00 && c <= 0xD7A3)
			{
				return true;
			}

			// 히라가나 / 가타카나(장음 기호 ー 포함) / 한자
			if ((c >= 0x3041 && c <= 0x3096) || (c >= 0x30A1 && c <= 0x30FA) || c == 0x30FC || (c >= 0x4E00 && c <= 0x9FFF))
			{
				return true;
			}

			return false;
		}

		// 금칙어를 포함하면 막는다 — 영문은 대소문자를 가리지 않는다.
		private static bool containsBanWord(string nickname)
		{
			Dictionary<int, Table_BanWord.Row>.Enumerator e = Table_BanWord.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				string word = e.Current.Value.Word;
				if (string.IsNullOrEmpty(word) == true)
				{
					continue;
				}

				if (nickname.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}

			return false;
		}

		// 비속어도 같은 방식으로 본다 — 목록만 다르다(외부 목록을 가져와 교체한다).
		private static bool containsBadWord(string nickname)
		{
			Dictionary<int, Table_BadWord.Row>.Enumerator e = Table_BadWord.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				string word = e.Current.Value.Word;
				if (string.IsNullOrEmpty(word) == true)
				{
					continue;
				}

				if (nickname.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}

			return false;
		}
	}
}
