using System.Collections.Generic;
using EDT;
using ProjectOne.Event;
using ProjectOne.ServerData;
using ProjectOne.Unit;

namespace ProjectOne.UserData
{
	// 캐릭터 도메인 모델 — 보유 캐릭터 목록 + 선택 + 캐릭터별 장착(아이템 3슬롯).
	// 캐릭터와 아이템 세트를 저장·관리한다. CharacterData(직렬화 원본)와 _index(빠른 조회)를 동기 유지하며, 변경 시 자체 저장.
	public sealed class Loadout
	{
		// EquipmentAspect 와 동일한 source 태그
		private const string EquipmentSource = "Equipment";

		private readonly CharacterData _data;
		private readonly Dictionary<int, OwnedCharacter> _index = new Dictionary<int, OwnedCharacter>();

		public Loadout(CharacterData data)
		{
			_data = (data != null) ? data : new CharacterData();
			buildIndex();
		}

		// ── 보유 / 획득 ───────────────────────────────────────────────

		public bool Has(int characterId)
		{
			return _index.ContainsKey(characterId);
		}

		public OwnedCharacter GetOwned(int characterId)
		{
			OwnedCharacter oc;
			if (_index.TryGetValue(characterId, out oc) == true)
			{
				return oc;
			}

			return null;
		}

		// 캐릭터 카드 획득 — 없으면 생성, 있으면 중복 카운트 누적(등급 상승 로직은 추후 dupCount 사용)
		public void Add(int characterId)
		{
			if (characterId <= 0)
			{
				return;
			}

			OwnedCharacter oc;
			if (_index.TryGetValue(characterId, out oc) == false)
			{
				oc = makeCharacter(characterId);
				_data.characters.Add(oc);
				_index.Add(characterId, oc);
			}
			else
			{
				oc.dupCount += 1;
			}

			save();
			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
		}

		// ── 선택 ──────────────────────────────────────────────────────

		public int Selected
		{
			get { return _data.selectedCharacterId; }
		}

		public bool TrySelect(int characterId)
		{
			if (Has(characterId) == false)
			{
				return false;
			}

			if (_data.selectedCharacterId == characterId)
			{
				return true;
			}

			_data.selectedCharacterId = characterId;
			save();
			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
			return true;
		}

		// ── 장착 (캐릭터별) ───────────────────────────────────────────

		public int GetSlot(int characterId, EquipmentType slot)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return 0;
			}

			switch (slot)
			{
				case EquipmentType.Weapon:    return oc.preset.weaponItemId;
				case EquipmentType.Armor:     return oc.preset.armorItemId;
				case EquipmentType.Accessory: return oc.preset.accessoryItemId;
				default: return 0;
			}
		}

		// 슬롯에 아이템 장착 — 타입 일치 + 보유 검증 후 설정
		public bool TrySetSlot(int characterId, EquipmentType slot, int itemId)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return false;
			}

			Table_Equipment.Row row = Table_Equipment.Get(itemId);
			if (row == null || row.EquipmentType != slot)
			{
				return false;
			}

			if (Account.Instance.Inventory.Has(itemId) == false)
			{
				return false;
			}

			setSlotValue(oc, slot, itemId);
			return true;
		}

		public void ClearSlot(int characterId, EquipmentType slot)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return;
			}

			setSlotValue(oc, slot, 0);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildIndex()
		{
			_index.Clear();
			for (int i = 0; i < _data.characters.Count; i++)
			{
				OwnedCharacter oc = _data.characters[i];
				if (oc == null || oc.characterId <= 0)
				{
					continue;
				}

				if (oc.preset == null)
				{
					oc.preset = new EquipPreset();
				}

				_index[oc.characterId] = oc;
			}
		}

		private static OwnedCharacter makeCharacter(int characterId)
		{
			OwnedCharacter oc = new OwnedCharacter();
			oc.characterId = characterId;
			oc.grade = 1;
			oc.level = 1;
			oc.exp = 0;
			oc.awakenLevel = 0;
			oc.dupCount = 0;
			oc.preset = new EquipPreset();
			return oc;
		}

		private void setSlotValue(OwnedCharacter oc, EquipmentType slot, int itemId)
		{
			switch (slot)
			{
				case EquipmentType.Weapon:    oc.preset.weaponItemId = itemId; break;
				case EquipmentType.Armor:     oc.preset.armorItemId = itemId; break;
				case EquipmentType.Accessory: oc.preset.accessoryItemId = itemId; break;
				default: return;
			}

			save();
			EventManager.Instance.Publish(new PresetChangeEvent(oc.characterId, slot, itemId));
			reapplyHero(oc.characterId);
		}

		// 해당 캐릭터가 현재 활성 Hero 면 장비 Aspect 즉시 갱신 (아니면 다음 스폰 시 ApplyAll 반영)
		private void reapplyHero(int characterId)
		{
			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				Hero hero = heroes[i] as Hero;
				if (hero != null && hero.GetTableID() == characterId)
				{
					HeroAspectRegistry.Instance.Reapply(hero, EquipmentSource);
				}
			}
		}

		private void save()
		{
			ServerDataSystem.Repository.Save(ServerDataSystem.KeyCharacter, _data);
		}
	}
}
