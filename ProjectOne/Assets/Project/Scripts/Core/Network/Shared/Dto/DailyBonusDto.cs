using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 출석 종류 1개의 진행도(직렬화 DTO).
	// typeId 는 (int)EDT.DailyBonusType — 서버와 공유하는 스키마라 enum 이 아니라 정수로 눕힌다.
	[System.Serializable]
	public class DailyBonusProgressDto
	{
		public int typeId;

		// 다음에 받을 일차 (1부터). 주기 끝을 넘으면 1로 돌아온다.
		public int dayCount;

		// 마지막으로 수령한 날 (DailyReset.GetResetDay 기준). 오늘 값과 다르면 아직 안 받은 것이다.
		public int lastClaimedDay;
	}

	// 출석 직렬화 DTO — 서버-클라 공유 영속 스키마. 클라는 DailyBonusBook 으로 변환해 사용한다.
	// JsonUtility 가 Dictionary 직렬화 불가 → List 보관 (CurrencyDto 와 같은 관례).
	[System.Serializable]
	public class DailyBonusDto
	{
		public List<DailyBonusProgressDto> progress = new List<DailyBonusProgressDto>();
	}
}
