namespace ProjectOne.Shared
{
	// 장착(로드아웃) 관련 패킷 — 캐릭터의 장착 슬롯 저장.
	// 클릭마다 보내지 않고, 편집 세션 종료(화면 닫기·앱 일시정지/종료) 시점에 dirty 면 1회만 전송한다.

	// 장착 저장 요청 — 8슬롯 전체를 싣는다. 인덱스는 EquipSlotTypes 정수값,
	// 값은 장비 인스턴스 UID. 서버가 보유 검증 후 갱신.
	[System.Serializable]
	public class SaveLoadoutRequest
	{
		public long[] slots = new long[LoadoutDto.SlotCount];
	}

	// 장착 저장 응답 — 성공 여부만 필요(상태는 클라가 이미 낙관적으로 반영).
	[System.Serializable]
	public class SaveLoadoutResponse : ServerResponse
	{
	}
}
