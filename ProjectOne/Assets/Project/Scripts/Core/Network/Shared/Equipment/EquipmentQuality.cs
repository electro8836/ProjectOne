namespace ProjectOne.Shared
{
	// 장비 품질의 단위. 클라·서버 공용.
	//
	// 품질은 소수 첫째 자리까지 쓴다(1.0% ~ 100.0%). 저장·전송·추첨은 0.1% 단위의 정수로 한다(782 = 78.2%) —
	// 보상 추첨을 클라와 서버가 같은 시드로 각자 굴리므로, 실수로 두면 플랫폼별 오차가 결과를 가를 수 있다.
	public static class EquipmentQuality
	{
		// 1% 가 저장 단위로 몇인가
		public const int Scale = 10;

		// 100.0%
		public const int Max = 100 * Scale;

		// 테이블에 적힌 퍼센트 값을 저장 단위로 바꾼다. Mathf.RoundToInt 와 같은 은행원 반올림(System.Math.Round 기본값).
		public static int FromPercent(float percent)
		{
			return (int)System.Math.Round(percent * Scale);
		}

		// 0 ~ 1 비율. 옵션 수치·전투력 계산이 구간 안의 위치로 쓴다.
		public static float ToRate(int quality)
		{
			return quality / (float)Max;
		}

		// 표시용 퍼센트 숫자 — "78.2". 정수로 조립해 실수 오차가 끼지 않는다.
		public static string Format(int quality)
		{
			return (quality / Scale).ToString() + "." + (quality % Scale).ToString();
		}
	}
}
