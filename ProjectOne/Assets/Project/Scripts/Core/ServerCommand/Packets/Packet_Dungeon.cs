using System.Collections.Generic;

namespace ProjectOne.ServerData
{
	// 던전 관련 패킷 — 클리어 보상 청구 등.

	// 던전 클리어 보상 청구 요청.
	// 클라는 "어느 던전을 클리어했는지"(mapId)만 보낸다 → 보상ID/확률은 서버가 Table_MapInfo 로 조회·계산(어뷰징 차단).
	[System.Serializable]
	public class ClaimDungeonRewardRequest
	{
		public int mapId;        // 클리어한 맵 — 서버가 ClearRewardIDs 를 조회
		public int characterId;  // 경험치 귀속 캐릭터
	}

	// 던전 클리어 보상 청구 응답 — 지급 결과(grants). 갱신 도메인 데이터는 베이스의 sync 로 전달.
	[System.Serializable]
	public class ClaimDungeonRewardResponse : ServerResponse
	{
		public List<RewardGrant> grants = new List<RewardGrant>();
	}

	// (테스트) 던전 클리어 — 클라는 mapId 만 보낸다. 서버가 exp+1000/gold+100 가산 저장 후 반환.
	[System.Serializable]
	public class DungeonClearRequest
	{
		public int mapId;
	}

	[System.Serializable]
	public class DungeonClearResponse : ServerResponse
	{
		public int exp;
		public int gold;
	}
}
