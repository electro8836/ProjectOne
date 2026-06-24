using System.Collections.Generic;
using EDT;
using ProjectOne.Event;
using ProjectOne.Shared;
using ProjectOne.Unit;

namespace ProjectOne.UserData
{
	// 캐릭터 도메인 모델 — 보유 캐릭터 목록 + 선택 + 캐릭터별 장착(아이템 3슬롯)(인메모리).
	// 공유 DTO(CharacterDto)를 받아 런타임 OwnedCharacter 로 변환 보유하고, 저장/전송 시 ToDto() 로 역변환한다.
	// 영속은 서버(Backnd 함수)가 담당, 변경 시 알림만 발행.
	public sealed class Loadout
	{
		// EquipmentAspect 와 동일한 source 태그
		private const string EquipmentSource = "Equipment";

		// 순서 보존용 목록 + 빠른 조회용 인덱스(동일 OwnedCharacter 참조 공유)
		private readonly List<OwnedCharacter> _characters = new List<OwnedCharacter>();
		private readonly Dictionary<int, OwnedCharacter> _index = new Dictionary<int, OwnedCharacter>();
		private int _selectedCharacterId;

		// 장착 변경(프리셋) 누적 플래그 — 클릭마다 서버 전송하지 않고, flush 시점에 dirty 면 1회만 저장한다.
		private bool _dirty;

		public Loadout(CharacterDto dto)
		{
			buildFromDto(dto);
		}

		// ── 보유 / 획득 ───────────────────────────────────────────────

		public bool Has(int characterId)
		{
			return _index.ContainsKey(characterId);
		}

		// 전체 보유 캐릭터 조회 (읽기 전용) — 디버그/조회용
		public IReadOnlyList<OwnedCharacter> GetAll()
		{
			return _characters;
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
				_characters.Add(oc);
				_index.Add(characterId, oc);
			}
			else
			{
				oc.dupCount += 1;
			}

			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
		}

		// ── 경험치 / 레벨업 ───────────────────────────────────────────

		// 경험치 누적만 한다(레벨은 자동 상승하지 않음 — 레벨업은 레벨업 버튼/서버 함수로만 +1). 변경 시 알림.
		public void AddExp(int characterId, int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return;
			}

			oc.exp += amount;
			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
		}

		// 레벨업 적용 — 서버 응답(또는 오프라인 로컬 처리) 후 호출. 레벨/경험치를 권위값으로 갱신하고 알림.
		public void ApplyLevelup(int characterId, int newLevel, int newExp)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return;
			}

			oc.level = newLevel;
			oc.exp = newExp;
			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
		}

		// 경험치를 권위값으로 갱신(레벨 불변) — 던전 보상 등 서버 가산 결과를 로컬에 반영하고 알림.
		public void SetExp(int characterId, int newExp)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return;
			}

			oc.exp = newExp;
			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
		}

		// 특성슬롯(slotIndex 1~5)의 현재 레벨 — 기본 1 + 장착 아이템의 TraitSlotValue 합(최대 5).
		public int GetTraitLevel(int characterId, int slotIndex)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return 1;
			}

			int bonus = traitBonus(oc.preset.weaponItemId, slotIndex)
				+ traitBonus(oc.preset.armorItemId, slotIndex)
				+ traitBonus(oc.preset.accessoryItemId, slotIndex);

			int level = 1 + bonus;
			if (level < 1) { level = 1; }

			if (level > 5) { level = 5; }

			return level;
		}

		// 아이템이 해당 슬롯(slotIndex)을 올리는 옵션을 가졌으면 그 증가량, 아니면 0.
		private static int traitBonus(int itemId, int slotIndex)
		{
			if (itemId <= 0)
			{
				return 0;
			}

			Table_Equipment.Row row = Table_Equipment.Get(itemId);
			if (row == null || row.TraitSlotIndex != slotIndex)
			{
				return 0;
			}

			return row.TraitSlotValue;
		}

		// ── 선택 ──────────────────────────────────────────────────────

		public int Selected
		{
			get { return _selectedCharacterId; }
		}

		// 장착 변경이 서버에 미반영(dirty)인지 — flush 코디네이터가 확인한다.
		public bool IsDirty
		{
			get { return _dirty; }
		}

		// 서버 저장 성공 후 호출 — dirty 해제.
		public void MarkSynced()
		{
			_dirty = false;
		}

		public bool TrySelect(int characterId)
		{
			if (Has(characterId) == false)
			{
				return false;
			}

			if (_selectedCharacterId == characterId)
			{
				return true;
			}

			_selectedCharacterId = characterId;
			_dirty = true;
			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
			return true;
		}

		// ── 장착 (캐릭터별) ───────────────────────────────────────────

		public int GetSlot(int characterId, EquipmentTypes slot)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return 0;
			}

			switch (slot)
			{
				case EquipmentTypes.Weapon:    return oc.preset.weaponItemId;
				case EquipmentTypes.Armor:     return oc.preset.armorItemId;
				case EquipmentTypes.Accessory: return oc.preset.accessoryItemId;
				default: return 0;
			}
		}

		// 슬롯에 아이템 장착 — 타입 일치 + 보유 검증 후 설정
		public bool TrySetSlot(int characterId, EquipmentTypes slot, int itemId)
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

		public void ClearSlot(int characterId, EquipmentTypes slot)
		{
			OwnedCharacter oc = GetOwned(characterId);
			if (oc == null)
			{
				return;
			}

			setSlotValue(oc, slot, 0);
		}

		// 직렬화 DTO 로 변환 — 저장/전송 시 사용
		public CharacterDto ToDto()
		{
			CharacterDto dto = new CharacterDto();
			dto.selectedCharacterId = _selectedCharacterId;
			for (int i = 0; i < _characters.Count; i++)
			{
				OwnedCharacter src = _characters[i];
				OwnedCharacterDto entry = new OwnedCharacterDto();
				entry.characterId = src.characterId;
				entry.grade = src.grade;
				entry.level = src.level;
				entry.exp = src.exp;
				entry.awakenLevel = src.awakenLevel;
				entry.dupCount = src.dupCount;
				entry.preset.weaponItemId = src.preset.weaponItemId;
				entry.preset.armorItemId = src.preset.armorItemId;
				entry.preset.accessoryItemId = src.preset.accessoryItemId;

				dto.characters.Add(entry);
			}

			return dto;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 공유 DTO → 런타임 OwnedCharacter 변환
		private void buildFromDto(CharacterDto dto)
		{
			_characters.Clear();
			_index.Clear();
			if (dto == null)
			{
				return;
			}

			_selectedCharacterId = dto.selectedCharacterId;
			for (int i = 0; i < dto.characters.Count; i++)
			{
				OwnedCharacterDto src = dto.characters[i];
				if (src == null || src.characterId <= 0)
				{
					continue;
				}

				OwnedCharacter oc = new OwnedCharacter();
				oc.characterId = src.characterId;
				oc.grade = src.grade;
				oc.level = src.level;
				oc.exp = src.exp;
				oc.awakenLevel = src.awakenLevel;
				oc.dupCount = src.dupCount;
				if (src.preset != null)
				{
					oc.preset.weaponItemId = src.preset.weaponItemId;
					oc.preset.armorItemId = src.preset.armorItemId;
					oc.preset.accessoryItemId = src.preset.accessoryItemId;
				}

				_characters.Add(oc);
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

		private void setSlotValue(OwnedCharacter oc, EquipmentTypes slot, int itemId)
		{
			switch (slot)
			{
				case EquipmentTypes.Weapon:    oc.preset.weaponItemId = itemId; break;
				case EquipmentTypes.Armor:     oc.preset.armorItemId = itemId; break;
				case EquipmentTypes.Accessory: oc.preset.accessoryItemId = itemId; break;
				default: return;
			}

			_dirty = true;
			EventManager.Instance.Publish(new PresetChangeEvent(oc.characterId, slot, itemId));
			reapplyHero(oc.characterId);
		}

		// 해당 아이템을 장착 중인 활성 Hero 의 장비 Aspect 즉시 갱신 (강화 등 아이템 수치 변경 반영)
		public void ReapplyEquipped(int itemId)
		{
			if (itemId <= 0)
			{
				return;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				Hero hero = heroes[i] as Hero;
				if (hero == null)
				{
					continue;
				}

				OwnedCharacter oc = GetOwned(hero.GetTableID());
				if (oc == null)
				{
					continue;
				}

				if (oc.preset.weaponItemId == itemId || oc.preset.armorItemId == itemId || oc.preset.accessoryItemId == itemId)
				{
					HeroAspectRegistry.Instance.Reapply(hero, EquipmentSource);
				}
			}
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

	}
}
