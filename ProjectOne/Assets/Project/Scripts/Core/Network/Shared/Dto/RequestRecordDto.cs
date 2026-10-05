namespace ProjectOne.Shared
{
	// 마지막으로 처리한 요청 1건(직렬화 DTO) — USER_REQUEST 의 Data 컬럼.
	// 클라가 통신 실패로 같은 요청을 다시 보냈을 때, 서버가 두 번 반영하지 않고 저장해 둔 응답을 돌려주는 데 쓴다.
	[System.Serializable]
	public class RequestRecordDto
	{
		public string rid;		// 요청 번호 — 클라가 요청마다 새로 만든다. 재전송은 같은 번호를 쓴다
		public string action;	// 어떤 요청이었는지(확인용)
		public string result;	// 그때 돌려준 응답 JSON 원문
	}
}
