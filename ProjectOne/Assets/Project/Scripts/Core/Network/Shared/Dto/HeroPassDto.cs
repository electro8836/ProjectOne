using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 활동 유형 1개의 누적 횟수(직렬화 DTO). typeId 는 (int)EDT.HeroPassExpType.
	[System.Serializable]
	public class HeroPassCounterDto
	{
		public int typeId;
		public int count;
	}

	// 히어로패스 저장 DTO — 서버-클라 공유 영속 스키마.
	//
	// 시즌은 계정 생성일(startResetDay)부터 35일 주기다. seasonIndex 가 지금 시즌과 다르면
	// 경험치·카운터·수령 기록·구매 여부를 전부 초기화한다(HeroPassRules.EnsureSeason).
	[System.Serializable]
	public class HeroPassDto
	{
		public int startResetDay;
		public int seasonIndex;

		public int exp;
		public bool purchased;

		public List<HeroPassCounterDto> counters = new List<HeroPassCounterDto>();

		// 수령한 레벨 목록 — 일반/추가 보상 따로
		public List<int> claimedNormal = new List<int>();
		public List<int> claimedAdvanced = new List<int>();
	}
}
