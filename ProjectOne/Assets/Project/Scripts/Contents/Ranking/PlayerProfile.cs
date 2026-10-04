using ProjectOne.Items;
using ProjectOne.Shared;

namespace ProjectOne.Ranking
{
	// 플레이어 정보 팝업이 그리는 스냅샷. 내 것이든 남의 것이든 같은 모양으로 넘긴다 —
	// 팝업은 Account 를 보지 않고 이것만 그린다.
	public sealed class PlayerProfile
	{
		public string playerId;
		public string playerName;
		public int level;			// 캐릭터 레벨(마스터리 제외)
		public int masteryLevel;	// 전 마스터리 레벨 합
		public int battlePower;		// 현재 전투력
		public int bestBattlePower;	// 최고 전투력(랭킹 점수)
		public int weaponCostumeId;	// 0 이면 미착용
		public int bodyCostumeId;	// 0 이면 미착용(기본 바디)

		// 인덱스는 EquipSlotTypes 값이다(0번은 비워 둔다). 빈 칸은 null.
		public readonly EquipmentInstance[] equipped = new EquipmentInstance[LoadoutDto.SlotCount];
	}
}
