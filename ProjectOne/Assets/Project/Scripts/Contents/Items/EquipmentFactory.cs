using EDT;
using UnityEngine;

namespace ProjectOne.Items
{
	// 장비 인스턴스 생성기 (아이템 설계 7장).
	//
	// 등급·품질 **추첨**은 서버와 공유하는 코어(ProjectOne.Shared.EquipmentRoll / RewardRoller)가 맡는다.
	// 여기는 이미 정해진 등급·품질로 Level 1 인스턴스를 만드는 일만 한다 — 로컬 추첨 결과와 서버 지급분이 같은 입구를 쓴다.
	public static class EquipmentFactory
	{
		// 등급·품질을 전부 지정해 만든다. 추첨이 끼지 않으므로 같은 인자로는 항상 같은 인스턴스가 나온다.
		public static EquipmentInstance CreateExact(int itemId, ItemGradeType grade, int quality)
		{
			if (Table_Equipment.Get(itemId) == null)
			{
				Debug.LogError($"[EquipmentFactory] 장비 아이템이 아닙니다: {itemId}");
				return null;
			}

			EquipmentInstance instance = new EquipmentInstance();
			instance.itemId = itemId;
			instance.grade = grade;
			instance.level = 1;
			instance.quality = quality;
			instance.equippedSlot = EquipSlotTypes.None;
			return instance;
		}
	}
}
