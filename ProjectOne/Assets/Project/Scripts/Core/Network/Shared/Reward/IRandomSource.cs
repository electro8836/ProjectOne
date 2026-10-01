namespace ProjectOne.Shared
{
	// 난수 공급원 — 보상 추첨 코어가 클라와 서버에서 같은 코드로 돌게 한다.
	// 정수만 공급한다. 확률 판정까지 정수(ppm)로 해서 플랫폼별 실수 오차가 결과를 가르지 않게 한다.
	public interface IRandomSource
	{
		// [minInclusive, maxExclusive) 균등 정수.
		int Range(int minInclusive, int maxExclusive);
	}
}
