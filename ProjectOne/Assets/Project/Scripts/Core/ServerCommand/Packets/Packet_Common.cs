namespace ProjectOne.ServerData
{
	// 통신 공통 기반 — 모든 서버 액션이 공유하는 베이스/페이로드/결과 타입.

	// 모든 서버 액션 응답의 공통 베이스 — 성공 여부 + 갱신 도메인 데이터(sync).
	// 각 액션 응답은 이걸 상속하고 고유 결과 필드만 추가한다.
	// (Unity JsonUtility 는 상속 public 필드도 직렬화 → Backnd JSON 과 호환)
	[System.Serializable]
	public class ServerResponse
	{
		public bool success;            // 실패(비용 부족·검증 실패 등) 시 false
		public string error;            // 실패 사유(선택)
	}
}
