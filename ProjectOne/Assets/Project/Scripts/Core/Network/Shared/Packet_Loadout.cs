namespace ProjectOne.Shared
{
	// 장착(로드아웃) 관련 패킷 — 선택 캐릭터의 장착 프리셋 저장.
	// 클릭마다 보내지 않고, 편집 세션 종료(화면 닫기·앱 일시정지/종료) 시점에 dirty 면 1회만 전송한다.

	// 장착 저장 요청 — 선택 캐릭터 1명의 3슬롯 + 메인 선택(selectedCharacterId)을 싣는다. 서버가 보유 검증 후 갱신.
	[System.Serializable]
	public class SaveLoadoutRequest
	{
		public int characterId;
		public int weaponItemId;
		public int armorItemId;
		public int accessoryItemId;
		public int selectedCharacterId;
	}

	// 장착 저장 응답 — 성공 여부만 필요(상태는 클라가 이미 낙관적으로 반영).
	[System.Serializable]
	public class SaveLoadoutResponse : ServerResponse
	{
	}
}
