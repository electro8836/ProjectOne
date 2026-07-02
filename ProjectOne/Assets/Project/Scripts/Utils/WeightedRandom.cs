using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Utils
{
	// Weight 기반 가중 랜덤 추출 유틸.
	public static class WeightedRandom
	{
		// weights 합을 기준으로 Random.Range(0,total) 롤 → 당첨 인덱스 반환.
		// 합이 0 이하면(모두 0 또는 빈 목록) -1 반환.
		public static int PickIndex(IReadOnlyList<int> weights)
		{
			if (weights == null || weights.Count == 0)
			{
				return -1;
			}

			int total = 0;
			for (int i = 0; i < weights.Count; i++)
			{
				if (weights[i] > 0)
				{
					total += weights[i];
				}
			}

			if (total <= 0)
			{
				return -1;
			}

			int roll = Random.Range(0, total);
			int acc = 0;
			for (int i = 0; i < weights.Count; i++)
			{
				if (weights[i] <= 0)
				{
					continue;
				}

				acc += weights[i];
				if (roll < acc)
				{
					return i;
				}
			}

			return weights.Count - 1;
		}
	}
}
