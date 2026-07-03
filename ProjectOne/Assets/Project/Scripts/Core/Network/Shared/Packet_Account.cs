namespace ProjectOne.Shared
{
	// 계정 관련 패킷 — 로그인 후 전체 계정 데이터 로드 등.

	// 계정 데이터 요청 — 입력 없음(내 계정은 서버가 세션으로 식별). 파라미터가 없어도 클래스로 정의한다.
	[System.Serializable]
	public class GetUserDataRequest
	{
	}

	// 계정 데이터 응답 — 로그인 스냅샷 번들. 서버가 CURRENCY/INVENTORY/CHARACTER 를 읽어 한 응답으로 조립한다.
	// 신규 계정이면 서버가 기본값(스타터)을 생성해 채워 반환한다.
	// 향후 스킬/프로필도 같은 응답에 필드만 추가한다(테이블 분리 ≠ 패킷 분리).
	[System.Serializable]
	public class GetUserDataResponse : ServerResponse
	{
		public CurrencyDto currency;
		public InventoryDto inventory;
		public CharacterDto character;
		public CardSkillBookDto cardSkill;
		public ClearedDungeonsDto clearedDungeons;
	}
}
