using System.Collections.Generic;

namespace ProjectOne.ServerData
{
	// 뽑기 관련 패킷 — 장비 뽑기 등.

	// 장비 뽑기 요청 — 클라는 "몇 회 뽑는지"(drawCount)만 보낸다.
	// 비용/확률은 서버가 계산(어뷰징 차단). 현재 로컬 구현은 균등 랜덤.
	[System.Serializable]
	public class DrawEquipmentRequest
	{
		public int drawCount;  // 뽑기 횟수 (예: 1, 10)
	}

	// 장비 뽑기 응답 — 지급 결과(grants). 갱신 도메인 데이터는 베이스의 sync 로 전달.
	[System.Serializable]
	public class DrawEquipmentResponse : ServerResponse
	{
		public List<RewardGrant> grants = new List<RewardGrant>();
	}
}
