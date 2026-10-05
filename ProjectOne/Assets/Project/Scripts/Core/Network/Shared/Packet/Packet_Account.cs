namespace ProjectOne.Shared
{
	// 계정 관련 패킷 — 로그인 후 전체 계정 데이터 로드 등.

	// 계정 데이터 요청 — 입력 없음(내 계정은 서버가 세션으로 식별). 파라미터가 없어도 클래스로 정의한다.
	[System.Serializable]
	public class GetUserDataRequest
	{
	}

	// 계정 데이터 응답 — 로그인 스냅샷 번들. 서버가 USER_* 도메인 테이블을 읽어 한 응답으로 조립한다.
	// 신규 계정이면 서버가 기본값(스타터)을 생성해 채워 반환한다.
	// 도메인이 늘면 같은 응답에 필드만 추가한다(테이블 분리 ≠ 패킷 분리).
	[System.Serializable]
	public class GetUserDataResponse : ServerResponse
	{
		public CurrencyDto currency;
		public InventoryDto inventory;
		public LoadoutDto loadout;
		public DungeonProgressDto dungeonProgress;
		public CostumeDto costume;
		public MasteryDto mastery;
		public QuestDto quest;
		public PetDto pet;
		public DailyBonusDto dailyBonus;
		public HeroPassDto heroPass;
		public ShopDto shop;
		public FieldSessionDto field;
		public string nickname;		// 뒤끝 계정 닉네임 — 없으면 서버가 Player + 8자리 숫자로 부여한다
		public int nicknameChangeCount;	// 닉네임을 바꾼 횟수 — 비용 판정(NicknameRules.GetCost)
	}

	// 닉네임 변경 요청 — 규칙·중복·비용은 서버가 판정한다.
	[System.Serializable]
	public class ChangeNicknameRequest
	{
		public string nickname;
	}

	[System.Serializable]
	public class ChangeNicknameResponse : ServerResponse
	{
		public string nickname;			// 바뀐 닉네임
		public int changeCount;			// 변경 후 누적 횟수
		public CurrencyAmountDto spent;	// 차감한 재화 — 무료 변경이면 null
	}
}
