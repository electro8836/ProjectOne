using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 다른 유저에게 보이는 내 정보 스냅샷 — 공개 테이블 USER_PROFILE 의 Data 컬럼.
	// 서버가 자기 데이터로 만든다 — 전투력이 바뀌는 펑션이 저장 뒤에 함께 갱신한다.
	[System.Serializable]
	public class PlayerProfileDto
	{
		public int level;				// 캐릭터 레벨(마스터리 제외)
		public int masteryLevel;		// 전 마스터리 레벨 합
		public int battlePower;			// 현재 전투력(서버 계산)
		public int bestBattlePower;		// 최고 전투력 — 랭킹 점수와 같다
		public int weaponCostumeId;		// 0 = 미착용
		public int bodyCostumeId;		// 0 = 미착용(기본 바디)

		// 장착 중인 장비만 담는다 — 부위는 equippedSlot.
		public List<EquipmentInstanceDto> equipped = new List<EquipmentInstanceDto>();
	}
}
