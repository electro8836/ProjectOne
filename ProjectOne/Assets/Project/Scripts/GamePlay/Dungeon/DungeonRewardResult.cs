using EDT;

namespace ProjectOne.Dungeon
{
	// 해석된 보상 1건 — Weight 로 등급(또는 재화 종류/수량)까지만 확정. 구체 아이템 특정은 이후.
	public readonly struct DungeonRewardResult
	{
		public readonly int RewardItemId;   // Table_RewardItem.ID (이름/아이콘/설명 표시용, 레이어1 결과)
		public readonly RewardTypes Type;   // 보상 종류 (편의 캐시)
		public readonly int SourceRowId;    // Table_Reward_* 당첨 Row.ID (등급/재화종류 정보 보관, 레이어2 결과)
		public readonly int Amount;         // Currency 롤 수량, 그 외 1
		public readonly bool IsBonus;       // 추가(엑스트라) 스테이지 보상 여부

		public DungeonRewardResult(int rewardItemId, RewardTypes type, int sourceRowId, int amount)
			: this(rewardItemId, type, sourceRowId, amount, false)
		{
		}

		public DungeonRewardResult(int rewardItemId, RewardTypes type, int sourceRowId, int amount, bool isBonus)
		{
			this.RewardItemId = rewardItemId;
			this.Type = type;
			this.SourceRowId = sourceRowId;
			this.Amount = amount;
			this.IsBonus = isBonus;
		}
	}
}
