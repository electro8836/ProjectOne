using System;

namespace ProjectOne.Shared
{
	// 일일 콘텐츠의 갱신일 번호 — 한국 시간 오전 6시에 하루가 넘어간다. 클라(DailyReset)와 서버가 같은 식을 쓴다.
	// 값이 달라졌다는 것이 곧 "하루가 넘어갔다" 는 뜻이다. 절대 날짜가 아니므로 비교에만 쓴다.
	public static class ResetDay
	{
		private const int ResetHour = 6;

		private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

		public static int FromUtc(DateTime utcNow)
		{
			DateTime nowKst = utcNow + KstOffset;

			// 경계 시각만큼 당기면 06시 이전은 전날로 밀려 날짜 하나가 곧 하루가 된다.
			return (int)(nowKst.AddHours(-ResetHour).Date - DateTime.MinValue).TotalDays;
		}
	}
}
