using System;

namespace ProjectOne.Utils
{
	// 일일 콘텐츠의 갱신 경계. 한국 시간 오전 6시에 하루가 넘어간다.
	//
	// TimeZoneInfo 를 쓰지 않는 이유 — 타임존 ID 가 플랫폼마다 달라("Korea Standard Time" vs "Asia/Seoul")
	// 모바일에서 조회에 실패한다. 한국은 서머타임이 없어 고정 오프셋으로 충분하다.
	//
	// 기준 시계는 기기의 UTC 다. 서버 시간 동기화가 붙으면 여기 한 곳만 갈아끼우면 된다.
	public static class DailyReset
	{
		private const int RESET_HOUR = 6;

		private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

		// 다음 갱신까지 남은 시간.
		public static TimeSpan GetRemaining()
		{
			DateTime nowKst = DateTime.UtcNow + KstOffset;
			DateTime todayReset = nowKst.Date.AddHours(RESET_HOUR);

			// 오늘 6시를 이미 지났으면 다음 경계는 내일 6시다.
			DateTime next = (nowKst < todayReset) ? todayReset : todayReset.AddDays(1);

			return next - nowKst;
		}

		// 오늘이 몇 번째 갱신일인지 — 경계로 자른 날짜의 일련번호.
		// 값이 달라졌다는 것이 곧 "하루가 넘어갔다" 는 뜻이다. 절대 날짜가 아니므로 비교에만 쓴다.
		public static int GetResetDay()
		{
			DateTime nowKst = DateTime.UtcNow + KstOffset;

			// 경계 시각만큼 당기면 06시 이전은 전날로 밀려 날짜 하나가 곧 하루가 된다.
			return (int)(nowKst.AddHours(-RESET_HOUR).Date - DateTime.MinValue).TotalDays;
		}
	}
}
