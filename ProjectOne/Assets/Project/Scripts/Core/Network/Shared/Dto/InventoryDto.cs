using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 스택 아이템 1종(직렬화 DTO) — 재료·소모품·수집품.
	// 장비는 스택이 아니라 인스턴스 단위이므로 EquipmentInstanceDto 가 따로 있다.
	[System.Serializable]
	public class OwnedItemDto
	{
		public int itemId;
		public int count;
	}

	// 장비 인스턴스(직렬화 DTO) — 아이템 설계 4장의 저장 필드 그대로.
	// 옵션 수치는 저장하지 않는다. 테이블 조회로 매번 재계산한다.
	[System.Serializable]
	public class EquipmentInstanceDto
	{
		public long uid;
		public int itemId;
		public int grade;			// ItemGradeType
		public int level = 1;
		public int quality;			// 0.1% 단위 (EquipmentQuality)
		public int equippedSlot;	// EquipSlotTypes (0 = 미착용)
		public bool inStash;		// 보관함에 넣어 둔 장비 — 인벤토리 개수에서 빠진다
		public bool locked;			// 잠근 장비 — 분해되지 않는다
	}

	// 일괄·자동 분해 조건. 등급은 여러 개를 고를 수 있어 비트로 둔다(비트 번호 = ItemGradeType 값).
	[System.Serializable]
	public class DecomposeSettingDto
	{
		public int gradeMask;
		public bool useQuality;		// 켜면 품질이 quality 이하인 장비만 대상이다
		public int quality;			// 0.1% 단위, 0 ~ EquipmentQuality.Max
		public bool auto;			// 획득 시 같은 조건으로 자동 분해
	}

	// 인벤토리 직렬화 DTO — 서버-클라 공유 영속 스키마. 클라는 Inventory 로 변환해 사용한다.
	[System.Serializable]
	public class InventoryDto
	{
		public List<OwnedItemDto> items = new List<OwnedItemDto>();
		public List<EquipmentInstanceDto> equipments = new List<EquipmentInstanceDto>();

		// 다음에 발급할 장비 UID. 서버 이관 전까지 클라가 채번한다(STEP 14).
		public long nextEquipmentUid = 1;

		// 상점에서 구매해 늘어난 칸 수. 최대 칸 수는 InventoryRules 가 기본값에 더해 구한다.
		public int inventoryCapacityBonus;
		public int stashCapacityBonus;

		// 분해 조건. 서버가 지급할 때 자동 분해 판정에 쓴다.
		public DecomposeSettingDto decomposeSetting = new DecomposeSettingDto();
	}
}
