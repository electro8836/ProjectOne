namespace ProjectOne.Shared
{
	// 모든 서버 함수 응답의 공통 베이스 — 성공 여부 + 실패 사유.
	// 각 함수 응답은 이걸 상속하고 고유 결과 필드만 추가한다.
	// public 필드만 사용 → Unity JsonUtility 와 서버 Newtonsoft.Json 양쪽 직렬화 호환.
	[System.Serializable]
	public class ServerResponse
	{
		public bool success;            // 실패(비용 부족·검증 실패 등) 시 false
		public string error;            // 실패 사유(선택)
	}
}
