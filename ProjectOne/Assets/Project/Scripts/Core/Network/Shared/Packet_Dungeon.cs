namespace ProjectOne.Shared
{
	// 던전 관련 패킷 — 클리어 보상 청구 등.

	// 던전 클리어 — 클라가 mapId + 전투한 캐릭터(characterId)를 보낸다. 서버가 보상 exp 를 해당 캐릭터에 가산 저장 후 반환.
	[System.Serializable]
	public class DungeonClearRequest
	{
		public int mapId;
		public int characterId;
	}

	// exp = 보상 가산 후 캐릭터의 누적 경험치(권위값).
	[System.Serializable]
	public class DungeonClearResponse : ServerResponse
	{
		public int exp;
	}
}
