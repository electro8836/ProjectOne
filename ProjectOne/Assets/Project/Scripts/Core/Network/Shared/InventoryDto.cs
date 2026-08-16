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
		public int purity;			// EquipPurity
		public int quality;
		public int equippedSlot;	// EquipSlotTypes (0 = 미착용)
	}

	// 인벤토리 직렬화 DTO — 서버-클라 공유 영속 스키마. 클라는 Inventory 로 변환해 사용한다.
	[System.Serializable]
	public class InventoryDto
	{
		public List<OwnedItemDto> items = new List<OwnedItemDto>();
		public List<EquipmentInstanceDto> equipments = new List<EquipmentInstanceDto>();

		// 다음에 발급할 장비 UID. 서버 이관 전까지 클라가 채번한다(STEP 14).
		public long nextEquipmentUid = 1;
	}
}
