namespace ProjectOne.Shared
{
	// 가챠(뽑기) 관련 패킷 — 장비 뽑기 요청/결과.

	// 장비 뽑기 요청 — 클라는 뽑기 종류(gachaId)와 횟수(count)만 보낸다.
	// 비용·확률·지급은 전부 서버가 차트로 판정한다(서버 권위).
	[System.Serializable]
	public class GachaDrawRequest
	{
		public int gachaId;
		public int count;
	}

	// 장비 뽑기 결과 — 뽑힌 장비 ID 목록과 등급, 소모/잔여 재화.
	// itemIds[i] 와 grades[i] 는 같은 추첨 결과를 가리킨다(인덱스 대응).
	[System.Serializable]
	public class GachaDrawResponse : ServerResponse
	{
		public int[] itemIds;
		public int[] grades;
		public int currencyId;
		public int spentAmount;
		public int remainingAmount;
	}
}
