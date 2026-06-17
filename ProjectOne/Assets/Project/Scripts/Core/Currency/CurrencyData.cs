using System.Collections.Generic;

namespace ProjectOne.ServerData
{
	// 보유 재화 1종 — currencyId 는 (int)CurrencyInfo, amount 는 보유 수량.
	[System.Serializable]
	public class CurrencyAmount
	{
		public int currencyId;
		public int amount;
	}

	// 재화 도메인 데이터 (직렬화 원본). JsonUtility 가 Dictionary 직렬화 불가 → List 보관.
	[System.Serializable]
	public class CurrencyData
	{
		public List<CurrencyAmount> amounts = new List<CurrencyAmount>();
	}
}
