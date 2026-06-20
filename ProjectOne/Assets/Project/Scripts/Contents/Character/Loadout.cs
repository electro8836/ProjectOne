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

		// 경험치 누적 → 누적량 기준으로 레벨 재계산(다중 레벨업·이월 자동). 변경 시 저장 + 알림.
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
			oc.level = levelFromExp(oc.exp);
			EventManager.Instance.Publish(new CharacterChangeEvent(characterId));
		}

		// Table_LevelExp: ID=레벨, TotalExperience=그 레벨 도달에 필요한 누적 경험치.
		// 누적 exp 로 도달 가능한 가장 높은 레벨 반환(최소 1, 최대레벨에서 멈춤). ID는 1부터 연속 가정.
		private static int levelFromExp(int totalExp)
		{
			int level = 1;
			while (true)
			{
				Table_LevelExp.Row next = Table_LevelExp.Get(level + 1);
				if (next == null || next.TotalExperience > totalExp)
				{
					break;
				}

				level++;
			}

			return level;
		}

		// ── 선택 ──────────────────────────────────────────────────────

		public int Selected
		{
			get { return _selectedCharacterId; }
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
