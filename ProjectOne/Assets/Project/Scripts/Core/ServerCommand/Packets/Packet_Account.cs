namespace ProjectOne.ServerData
{
	// 계정 관련 패킷 — 로그인 후 전체 계정 데이터 로드 등.

	// 계정 데이터 요청 — 입력 없음(내 계정은 서버가 세션으로 식별).
	[System.Serializable]
	public class GetUserDataRequest
	{
	}

	// 계정 데이터 응답 — 계정 전체(character/inventory/skill/currency)는 베이스의 sync 로 전달.
	// 신규 계정이면 서버가 기본값(스타터)을 생성해 채워 반환한다.
	// (테스트) exp/gold: 서버가 저장한 값을 그대로 반환. 정식 전환 시 sync 로 통합.
	[System.Serializable]
	public class GetUserDataResponse : ServerResponse
	{
		public int exp;
	}
}
