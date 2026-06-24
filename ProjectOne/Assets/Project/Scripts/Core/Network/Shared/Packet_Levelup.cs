namespace ProjectOne.Shared
{
	// 레벨업 관련 패킷 — 선택 캐릭터 1명을 다음 레벨로 올린다.
	// 서버가 경험치 충족 + 재료/재화 비용을 검증·차감한 뒤 레벨을 +1 하고 권위 레벨/경험치를 반환한다(서버 권위).

	// 레벨업 요청 — 대상 캐릭터 1명(페이로드 최소). 비용/필요경험치는 서버가 차트로 조회한다.
	[System.Serializable]
	public class LevelupRequest
	{
		public int characterId;
	}

	// 레벨업 응답 — 갱신된 레벨/경험치(비용 차감 결과는 클라가 동일 비용으로 미러 반영).
	[System.Serializable]
	public class LevelupResponse : ServerResponse
	{
		public int newLevel;
		public int newExp;
	}
}
