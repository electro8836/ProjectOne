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
		public AccountSyncData sync;    // 서버가 채운 갱신 도메인 데이터(없으면 null)
	}

	// 서버가 갱신한 도메인 데이터 묶음 — 어떤 액션이든 변경된 것만 채워 반환한다(없으면 null).
	// 호출자는 Account.ApplySync 로 non-null 인 도메인만 반영한다.
	[System.Serializable]
	public class AccountSyncData
	{
		public CharacterData character;
		public InventoryData inventory;
		public SkillData skill;
		public CurrencyData currency;
	}

	// 지급된 보상/뽑기 결과 1건 — 서버 권위 결과를 클라 연출(UI)로 전달하기 위한 결과 항목.
	// 던전 보상·뽑기 등 여러 액션이 공용으로 사용한다.
	[System.Serializable]
	public class RewardGrant
	{
		public int rewardType;  // (int)EDT.RewardTypes — JSON/서버 호환 위해 int 보관
		public int targetId;    // Experience=characterId / Gold=(int)CurrencyInfo / Material·Equipment=itemId
		public int amount;
	}
}
