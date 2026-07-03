namespace ProjectOne.Dungeon
{
	// 지급할 RewardItem(상자) 1건 — 상자 종류와 개수만 담는다. 실제 등급/재화수량은 이후 서버 오픈 시 결정.
	public readonly struct DungeonRewardResult
	{
		public readonly int RewardItemId;   // Table_RewardItem.ID (상자 종류, 이름/아이콘/설명 표시용)
		public readonly int Amount;         // 상자 개수
		public readonly bool IsBonus;       // 추가(엑스트라) 스테이지 보상 여부

		public DungeonRewardResult(int rewardItemId, int amount)
			: this(rewardItemId, amount, false)
		{
		}

		public DungeonRewardResult(int rewardItemId, int amount, bool isBonus)
		{
			this.RewardItemId = rewardItemId;
			this.Amount = amount;
			this.IsBonus = isBonus;
		}
	}
}
