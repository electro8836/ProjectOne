namespace ProjectOne.Shared
{
	// 보관함 패킷 — 보관함에 넣어 둔 장비 목록 저장.
	// 이동할 때마다 보내지 않고, 장비 화면 닫기·앱 일시정지/종료 시점에 dirty 면 1회만 전송한다.

	// 보관함 저장 요청 — 보관함에 있는 장비 UID 전체(최종 상태)를 싣는다. 서버가 보유·칸 수 검증 후 갱신.
	[System.Serializable]
	public class SaveStashRequest
	{
		public long[] stashUids;
	}

	// 보관함 저장 응답 — 성공 여부만 필요(상태는 클라가 이미 낙관적으로 반영).
	[System.Serializable]
	public class SaveStashResponse : ServerResponse
	{
	}
}
