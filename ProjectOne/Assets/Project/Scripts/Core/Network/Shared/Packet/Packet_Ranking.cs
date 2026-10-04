namespace ProjectOne.Shared
{
	// 랭킹 패킷 — 뒤끝 유저 리더보드(최고 전투력).
	//
	// 점수는 비공개 테이블 USER_RANKDATA 의 컬럼이고, 정보 스냅샷(PlayerProfileDto)은 공개 테이블 USER_PROFILE 에 따로 둔다.
	// 전투력은 서버가 계산한다(BattlePowerRules) — 전투력이 바뀌는 펑션이 저장 뒤에 스스로 갱신하므로 보고 패킷은 없다.

	[System.Serializable]
	public class RankEntryDto
	{
		public string inDate;		// 유저 식별자(gamerInDate) — 정보 조회에 그대로 쓴다
		public string nickname;
		public int rank;			// 0 이면 리더보드 미등록
		public int battlePower;		// 최고 전투력(랭킹 점수)
	}

	// 뒤끝 리더보드는 한 번에 50명까지만 읽힌다 — 100위까지를 두 번에 나눠 받는다.
	[System.Serializable]
	public class RankListRequest
	{
		public int offset;			// 0 = 1~50위, 50 = 51~100위
	}

	[System.Serializable]
	public class RankListResponse : ServerResponse
	{
		public RankEntryDto[] top;	// offset 부터 최대 50명
		public RankEntryDto mine;	// 첫 페이지(offset 0)에서만 채운다
	}

	[System.Serializable]
	public class RankProfileRequest
	{
		public string inDate;
	}

	[System.Serializable]
	public class RankProfileResponse : ServerResponse
	{
		public PlayerProfileDto profile;
	}
}
