using System.Collections.Generic;

namespace ProjectOne.Ranking
{
	// 랭킹 조회 결과 — 상위 목록(최대 100위)과 내 순위.
	public sealed class RankingResult
	{
		public readonly List<RankEntry> top = new List<RankEntry>(100);
		public RankEntry mine;
	}
}
