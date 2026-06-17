using UnityEngine;
using ProjectOne.ServerData;

namespace ProjectOne.Boot
{
	// 개발용 테스트 데이터 주입기 — 부트씬 GameObject 에 부착.
	// 인스펙터로 캐릭터+장착3슬롯+레벨을 구성하고, Awake 에서 메모리 Repository 로 교체한다.
	// "사용안함" 체크 시 교체하지 않아 평소대로 서버/로컬 데이터를 받는다.
	// 모든 Awake 는 Start 보다 먼저 실행되므로 GameBootstrapper.Start(흐름 시작) 이전에 교체가 끝난다.
	public class DevDataInjector : MonoBehaviour
	{
		// 장착 슬롯 1칸 설정 — 아이템 ID 와 강화 레벨
		[System.Serializable]
		public struct DevSlot
		{
			public int itemId;
			public int enhanceLevel;
		}

		[Header("사용안함 — 체크 시 서버/로컬 데이터 사용")]
		[SerializeField] private bool _disabled;

		[Header("캐릭터")]
		[SerializeField] private int _characterId;
		[SerializeField] private int _characterLevel = 1;

		[Header("장착 슬롯")]
		[SerializeField] private DevSlot _weapon;
		[SerializeField] private DevSlot _armor;
		[SerializeField] private DevSlot _accessory;

		private void Awake()
		{
			if (_disabled == true)
			{
				return;
			}

			if (_characterId <= 0)
			{
				Debug.LogWarning("[DevDataInjector] _characterId 가 설정되지 않아 개발 데이터를 주입하지 않습니다. (서버/로컬 데이터로 진행)");
				return;
			}

			InventoryData inventory = buildInventory();
			CharacterData character = buildCharacter();
			SkillData skill = new SkillData();

			ServerDataSystem.SetRepository(new DevDataRepository(character, inventory, skill));
			Debug.Log("[DevDataInjector] 개발 데이터 주입 활성 — Character:" + _characterId + ", Level:" + _characterLevel);
		}

		private InventoryData buildInventory()
		{
			InventoryData inventory = new InventoryData();
			addOwnedItem(inventory, _weapon);
			addOwnedItem(inventory, _armor);
			addOwnedItem(inventory, _accessory);
			return inventory;
		}

		private static void addOwnedItem(InventoryData inventory, DevSlot slot)
		{
			if (slot.itemId <= 0)
			{
				return;
			}

			OwnedItem item = new OwnedItem();
			item.itemId = slot.itemId;
			item.count = 1;
			item.enhanceLevel = slot.enhanceLevel;
			inventory.items.Add(item);
		}

		private CharacterData buildCharacter()
		{
			EquipPreset preset = new EquipPreset();
			preset.weaponItemId = _weapon.itemId;
			preset.armorItemId = _armor.itemId;
			preset.accessoryItemId = _accessory.itemId;

			OwnedCharacter oc = new OwnedCharacter();
			oc.characterId = _characterId;
			oc.grade = 1;
			oc.level = _characterLevel;
			oc.exp = 0;
			oc.awakenLevel = 0;
			oc.dupCount = 0;
			oc.preset = preset;

			CharacterData character = new CharacterData();
			character.characters.Add(oc);
			character.selectedCharacterId = _characterId;
			return character;
		}
	}
}
