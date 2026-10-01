using ProjectOne.Shared;
using UnityEngine;

namespace ProjectOne.Utils
{
	// 공유 추첨 코어(ProjectOne.Shared)에 UnityEngine.Random 을 공급한다.
	public sealed class UnityRandomSource : IRandomSource
	{
		public static readonly UnityRandomSource Instance = new UnityRandomSource();

		private UnityRandomSource() { }

		public int Range(int minInclusive, int maxExclusive)
		{
			return Random.Range(minInclusive, maxExclusive);
		}
	}
}
