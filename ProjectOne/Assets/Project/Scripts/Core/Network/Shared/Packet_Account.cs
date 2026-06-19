namespace ProjectOne.Shared
{
	// 계정 관련 패킷 — 로그인 후 전체 계정 데이터 로드 등.

	// 계정 데이터 요청 — 입력 없음(내 계정은 서버가 세션으로 식별). 파라미터가 없어도 클래스로 정의한다.
	[System.Serializable]
	public class GetUserDataRequest
	{
	}

	// 계정 데이터 응답 — 신규 계정이면 서버가 기본값(스타터)을 생성해 채워 반환한다.
	// (테스트) exp: 서버가 저장한 값을 그대로 반환. 정식 전환 시 도메인 데이터로 통합.
	[System.Serializable]
	public class GetUserDataResponse : ServerResponse
	{
		public int exp;
	}
}
