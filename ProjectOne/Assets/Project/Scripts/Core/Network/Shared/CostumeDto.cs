using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 코스튬 저장 DTO.
	//
	// 코스튬은 개별 인스턴스·강화·등급·순도가 없다. "보유했는가 / 무엇을 입었는가" 두 가지로 상태가 끝난다.
	// 그래서 장비처럼 uid 를 채번하지 않고 테이블 ID 목록만 싣는다.
	[System.Serializable]
	public class CostumeDto
	{
		public List<int> owned = new List<int>();

		// 0 = 미착용 — 장비로 낀 무기가 그대로 보인다.
		public int equippedWeaponId;

		// 0 = 미착용 — 기본 코스튬(IsDefault 행)이 적용된다.
		public int equippedBodyId;
	}
}
