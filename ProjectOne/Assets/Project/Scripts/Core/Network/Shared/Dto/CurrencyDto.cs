using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 보유 재화 1종(직렬화 DTO) — currencyId 는 (int)CurrencyInfo, amount 는 보유 수량.
	[System.Serializable]
	public class CurrencyAmountDto
	{
		public int currencyId;
		public int amount;
	}

	// 재화 직렬화 DTO — 서버-클라 공유 영속 스키마. 클라는 Wallet 으로 변환해 사용한다.
	// JsonUtility 가 Dictionary 직렬화 불가 → List 보관.
	[System.Serializable]
	public class CurrencyDto
	{
		public List<CurrencyAmountDto> amounts = new List<CurrencyAmountDto>();
	}
}
