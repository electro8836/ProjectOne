namespace ProjectOne.Shared
{
	// 시드 결정적 난수(SplitMix64) — 같은 시드면 클라(IL2CPP/Mono)와 서버(.NET)에서 같은 수열이 나온다.
	// UnityEngine.Random 과 System.Random 은 구현이 달라 시드 재현에 쓸 수 없다.
	public sealed class DeterministicRandom : IRandomSource
	{
		private ulong _state;

		public DeterministicRandom(ulong seed)
		{
			_state = seed;
		}

		public int Range(int minInclusive, int maxExclusive)
		{
			if (maxExclusive <= minInclusive)
			{
				return minInclusive;
			}

			ulong span = (ulong)((long)maxExclusive - minInclusive);
			return (int)((long)minInclusive + (long)(next() % span));
		}

		private ulong next()
		{
			_state += 0x9E3779B97F4A7C15UL;
			return Mix(_state);
		}

		// SplitMix64 의 출력 혼합 함수 — 시드 파생(KillSeed)에도 쓴다.
		public static ulong Mix(ulong z)
		{
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			return z ^ (z >> 31);
		}
	}

	// 필드 처치 1건의 시드 — 세션 시드에서 처치마다 독립 시드를 파생한다.
	//
	// 하나의 수열을 처치들이 이어 쓰면 클라가 어디선가 한 번만 더 굴려도 이후가 전부 어긋난다.
	// 처치 단위로 끊으면 순서·누락과 무관하게 서버가 그 처치만 재현할 수 있다.
	public static class KillSeed
	{
		public static ulong Derive(long sessionSeed, int killIndex, int monsterId)
		{
			ulong h = DeterministicRandom.Mix((ulong)sessionSeed);
			h = DeterministicRandom.Mix(h ^ ((ulong)(uint)killIndex * 0x9E3779B97F4A7C15UL));
			h = DeterministicRandom.Mix(h ^ ((ulong)(uint)monsterId * 0xC2B2AE3D27D4EB4FUL));
			return h;
		}
	}
}
