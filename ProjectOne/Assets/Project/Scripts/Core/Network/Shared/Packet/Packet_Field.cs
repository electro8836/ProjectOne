namespace ProjectOne.Shared
{
	// 필드 처치 배치 정산 패킷.

	// 처치 1건. 서버는 (세션 시드, killIndex, monsterId) 로 같은 추첨을 재현하고 pickedMask 의 보상만 지급한다.
	[System.Serializable]
	public class FieldKillDto
	{
		public int killIndex;
		public int monsterId;
		public int level;
		public int spawnRewardGroupId;		// MonsterSpawn.RewardGroupID (지역 드랍, 0 = 없음)
		public int expBonusPermille;		// 처치 시점의 Stat_ExpBonus (‰)
		public int goldBonusPermille;		// 처치 시점의 Stat_GoldDropBonus (‰)
		public int masteryId;				// 처치 시점 장착 무기의 마스터리(WeaponMastery, 0 = 미착용) — 경험치 적립 대상
		public long pickedMask;				// 획득한 보상 인덱스 비트(추첨 결과 순서 기준)
	}

	// 필드보스 처치 — 처치 즉시 서버가 하루 1회 제한을 확인하고 경험치·보상을 지급한다(배치 정산에서 빠진다).
	[System.Serializable]
	public class FieldBossKillRequest
	{
		public int fieldId;					// Table_Field ID — 같은 보스라도 필드마다 하루 1회
		public int spawnId;					// MonsterSpawn 행 ID (RespawnType.DailyReset)
		public int monsterId;
		public int level;
		public int expBonusPermille;
		public int goldBonusPermille;
		public int masteryId;
	}

	[System.Serializable]
	public class FieldBossKillResponse : ServerResponse
	{
		public int exp;							// 정산 후 누적 경험치(서버값)
		public GrantedRewardDto[] rewards;
		public EquipmentInstanceDto[] equipments;
		public FieldBossKillDto kill;			// 저장된 처치 기록 — 리스폰 판정에 반영한다
	}

	[System.Serializable]
	public class FieldSettleRequest
	{
		public long sessionSeed;
		public FieldKillDto[] kills;
	}

	// 로딩(맵 이동) 시점 — 남은 처치를 정산하고 새 세션(시드·epoch)을 받는다.
	[System.Serializable]
	public class FieldRotateRequest
	{
		public long sessionSeed;
		public FieldKillDto[] kills;
	}

	// 정산이 폐기돼도 success 이고 새 field 가 온다(error 에 폐기 사유). exp 는 정산이 없었으면 -1.
	[System.Serializable]
	public class FieldRotateResponse : ServerResponse
	{
		public int exp;
		public FieldSessionDto field;
	}

	// 실패해도 nextKillIndex 를 채운다 — 클라가 이미 반영된(또는 버려진) 처치를 정리하는 기준이다.
	[System.Serializable]
	public class FieldSettleResponse : ServerResponse
	{
		public int nextKillIndex;
		public int exp;		// 정산 후 누적 경험치(서버값)
	}
}
