namespace ProjectOne.UI
{
	// 재화 수량 줄임 표기 — 1,000 이상은 K, 100만 이상은 M, 10억 이상은 B (소수점 없이 내림).
	public static class CurrencyFormat
	{
		public static string Short(int amount)
		{
			if (amount >= 1_000_000_000)
			{
				return (amount / 1_000_000_000).ToString() + "B";
			}

			if (amount >= 1_000_000)
			{
				return (amount / 1_000_000).ToString() + "M";
			}

			if (amount >= 1_000)
			{
				return (amount / 1_000).ToString() + "K";
			}

			return amount.ToString();
		}
	}
}
