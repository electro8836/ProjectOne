namespace ProjectOne.Network
{
	// 서버 거절 사유(영문 개발자용 문자열) → 유저에게 보일 문구.
	// 자주 나오는 사유만 옮기고 나머지는 공통 문구로 둔다 — 원문은 호출부 로그에 남는다.
	public static class NetworkMessages
	{
		public const string Reconnecting = "재연결 중입니다.";

		// 타이틀로 돌려보낸 뒤 띄우는 팝업.
		public const string Title = "알림";
		public const string Confirm = "확인";
		public const string ConnectionFailed = "서버와의 연결이 실패했습니다.";

		private const string NotEnough = "재화 또는 재료가 부족합니다.";
		private const string Common = "요청을 처리하지 못했습니다.\n잠시 후 다시 시도해 주세요.";

		public static string ForRejected(string error)
		{
			if (string.IsNullOrEmpty(error) == false && error.StartsWith("not enough") == true)
			{
				return NotEnough;
			}

			return Common;
		}
	}
}
