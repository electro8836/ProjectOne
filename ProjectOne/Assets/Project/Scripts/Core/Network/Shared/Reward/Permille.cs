namespace ProjectOne.Shared
{
	// 퍼밀(‰) 정수 배율 — 보너스 스탯을 실수 대신 정수로 다뤄 클라·서버 계산이 정확히 같게 한다.
	public static class Permille
	{
		// value × (1000 + permille) / 1000 — 0.5 는 올림.
		public static int Apply(int value, int permille)
		{
			long scaled = (long)value * (1000 + permille);
			return (int)((scaled + 500) / 1000);
		}
	}
}
