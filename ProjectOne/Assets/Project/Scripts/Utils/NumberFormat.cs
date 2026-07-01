using System.Globalization;

namespace ProjectOne.Utils
{
	// 큰 수를 K/M 축약 문자열로 변환한다. (소수 1자리, 정수면 소수 생략)
	public static class NumberFormat
	{
		// 예: 999000→"999K", 1500→"1.5K", 2000000→"2M", 500→"500"
		public static string ToShort(int value)
		{
			if (value >= 1_000_000)
			{
				return trim(value / 1_000_000f) + "M";
			}

			if (value >= 1_000)
			{
				return trim(value / 1_000f) + "K";
			}

			return value.ToString(CultureInfo.InvariantCulture);
		}

		// 소수 1자리 표기, 정수면 소수부 생략
		private static string trim(float value)
		{
			if (value == (int)value)
			{
				return ((int)value).ToString(CultureInfo.InvariantCulture);
			}

			return value.ToString("0.0", CultureInfo.InvariantCulture);
		}
	}
}
