namespace ProjectOne.Ranking
{
	// 랭킹 목록의 한 줄. rank 가 0 이하면 리더보드 미등록(순위 외)이다.
	public readonly struct RankEntry
	{
		public readonly string playerId;
		public readonly int rank;
		public readonly string playerName;
		public readonly int battlePower;

		public RankEntry(string playerId, int rank, string playerName, int battlePower)
		{
			this.playerId = playerId;
			this.rank = rank;
			this.playerName = playerName;
			this.battlePower = battlePower;
		}
	}
}
