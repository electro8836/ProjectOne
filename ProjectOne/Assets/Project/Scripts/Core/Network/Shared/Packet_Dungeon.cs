namespace ProjectOne.Shared
{
	// 던전 관련 패킷 — 클리어 보상 청구 등.

	// (테스트) 던전 클리어 — 클라는 mapId 만 보낸다. 서버가 exp 가산 저장 후 반환.
	[System.Serializable]
	public class DungeonClearRequest
	{
		public int mapId;
	}

	[System.Serializable]
	public class DungeonClearResponse : ServerResponse
	{
		public int exp;
	}
}
