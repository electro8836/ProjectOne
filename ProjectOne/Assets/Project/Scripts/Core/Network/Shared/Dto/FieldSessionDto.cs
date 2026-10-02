using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 필드 처치 배치 정산 세션 (USER_FIELD 저장 + GetUserData 응답).
	//
	// 서버가 로그인마다 seed 를 새로 발급하고 epoch 를 올린다. killIndex 는 세션 안에서 0 부터 단조 증가하며
	// 서버는 nextKillIndex 부터 이어지는 배치만 받는다(재전송·순서 조작 차단).
	[System.Serializable]
	public class FieldSessionDto
	{
		public long seed;
		public int epoch;
		public int nextKillIndex;

		// 마지막 정산 시각(UTC, ms) — 처치 수 상한 검증용. 서버만 쓴다.
		public long lastSettleUnixMs;

		// 필드보스(DailyReset 스폰) 처치 기록 — 같은 (fieldId, spawnId) 는 resetDay 가 바뀌기 전까지 다시 잡을 수 없다.
		public List<FieldBossKillDto> bossKills = new List<FieldBossKillDto>();
	}

	[System.Serializable]
	public class FieldBossKillDto
	{
		public int fieldId;
		public int spawnId;
		public int resetDay;
	}
}
