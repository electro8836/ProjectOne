using System.Collections.Generic;
using EDT;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Shared;
using ProjectOne.Unit;

namespace ProjectOne.UserData
{
	// 로드아웃 도메인 모델 — 단일 캐릭터의 레벨·경험치 + 8슬롯 장착(인메모리).
	//
	// 슬롯이 담는 값은 아이템 ID 가 아니라 **장비 인스턴스 UID** 다. 같은 아이템이라도
	// 등급·강화·순도·품질이 다른 개체가 여럿 있을 수 있어 ID 로는 어느 것인지 특정할 수 없다.
	//
	// 영속은 서버(Backnd 함수)가 담당, 변경 시 알림만 발행.
	public sealed class Loadout
	{
		// EquipmentAspect 와 동일한 source 태그
		private const string EquipmentSource = "Equipment";

		// 인덱스 = EquipSlotTypes 정수값. 0번(None)은 사용하지 않는다. 0 = 미장착.
		private readonly long[] _slots = new long[LoadoutDto.SlotCount];
		private int _level = 1;
		private int _exp;

		// 장착 변경 누적 플래그 — 클릭마다 서버 전송하지 않고, flush 시점에 dirty 면 1회만 저장한다.
		private bool _dirty;

		public Loadout(LoadoutDto dto)
		{
			buildFromDto(dto);
		}

		// ── 레벨 / 경험치 ─────────────────────────────────────────────

		public int Level
		{
			get { return _level; }
		}

		public int Exp
		{
			get { return _exp; }
		}

		// 경험치 누적만 한다(레벨은 자동 상승하지 않음 — 레벨업은 서버 함수로만 +1). 변경 시 알림.
		public void AddExp(int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			_exp += amount;
			EventManager.Instance.Publish(new CharacterChangeEvent());
		}

		// 레벨업 적용 — 서버 응답 후 호출. 레벨/경험치를 권위값으로 갱신하고 알림.
		public void ApplyLevelup(int newLevel, int newExp)
		{
			_level = newLevel;
			_exp = newExp;
			EventManager.Instance.Publish(new CharacterChangeEvent());
		}

		// 경험치를 권위값으로 갱신(레벨 불변) — 던전 보상 등 서버 가산 결과를 로컬에 반영하고 알림.
		public void SetExp(int newExp)
		{
			_exp = newExp;
			EventManager.Instance.Publish(new CharacterChangeEvent());
		}

		// ── 동기화 상태 ───────────────────────────────────────────────

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

		// ── 장착 ──────────────────────────────────────────────────────

		// 슬롯에 장착된 장비 UID. 미장착이면 0.
		public long GetSlot(EquipSlotTypes slot)
		{
			if (isValidSlot(slot) == false)
			{
				return 0;
			}

			return _slots[(int)slot];
		}

		// 슬롯에 장착된 장비 인스턴스. 미장착이면 null.
		public EquipmentInstance GetEquipped(EquipSlotTypes slot)
		{
			long uid = GetSlot(slot);
			if (uid <= 0)
			{
				return null;
			}

			return Account.Instance.Inventory.GetEquipment(uid);
		}

		// 장비 인스턴스를 장착한다 — 슬롯은 인스턴스 자신이 안다. 기존 장착분은 자동 해제된다.
		public bool TryEquip(long uid)
		{
			EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(uid);
			if (instance == null)
			{
				return false;
			}

			Table_Equipment.Row row = instance.Equipment;
			if (row == null || isValidSlot(row.EquipSlotType) == false)
			{
				return false;
			}

			EquipmentInstance previous = GetEquipped(row.EquipSlotType);
			if (previous != null)
			{
				previous.equippedSlot = EquipSlotTypes.None;
			}

			instance.equippedSlot = row.EquipSlotType;
			setSlotValue(row.EquipSlotType, uid);
			return true;
		}

		public void Unequip(EquipSlotTypes slot)
		{
			if (isValidSlot(slot) == false)
			{
				return;
			}

			EquipmentInstance previous = GetEquipped(slot);
			if (previous != null)
			{
				previous.equippedSlot = EquipSlotTypes.None;
			}

			setSlotValue(slot, 0);
		}

		// 현재 장착 무기의 종류 — 무기 마스터리 역참조의 진입점. 미착용이면 None.
		public WeaponType EquippedWeaponType
		{
			get
			{
				EquipmentInstance weapon = GetEquipped(EquipSlotTypes.Weapon);
				if (weapon == null)
				{
					return WeaponType.None;
				}

				Table_Equipment.Row row = weapon.Equipment;
				return row != null ? row.WeaponType : WeaponType.None;
			}
		}

		// 장착 중인 장비가 바뀌었으면(강화·승급 등) 활성 Hero 의 장비 Aspect 를 즉시 갱신한다.
		public void ReapplyEquipped(long uid)
		{
			if (uid <= 0 || isEquipped(uid) == false)
			{
				return;
			}

			reapplyHero();
		}

		// 직렬화 DTO 로 변환 — 저장/전송 시 사용
		public LoadoutDto ToDto()
		{
			LoadoutDto dto = new LoadoutDto();
			dto.level = _level;
			dto.exp = _exp;
			for (int i = 0; i < LoadoutDto.SlotCount; i++)
			{
				dto.slots[i] = _slots[i];
			}

			return dto;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildFromDto(LoadoutDto dto)
		{
			for (int i = 0; i < _slots.Length; i++)
			{
				_slots[i] = 0;
			}

			if (dto == null)
			{
				_level = 1;
				_exp = 0;
				return;
			}

			_level = dto.level > 0 ? dto.level : 1;
			_exp = dto.exp;
			if (dto.slots == null)
			{
				return;
			}

			int count = dto.slots.Length < _slots.Length ? dto.slots.Length : _slots.Length;
			for (int i = 0; i < count; i++)
			{
				_slots[i] = dto.slots[i];
			}
		}

		private static bool isValidSlot(EquipSlotTypes slot)
		{
			return slot != EquipSlotTypes.None && (int)slot < LoadoutDto.SlotCount;
		}

		private bool isEquipped(long uid)
		{
			for (int i = 1; i < _slots.Length; i++)
			{
				if (_slots[i] == uid)
				{
					return true;
				}
			}

			return false;
		}

		private void setSlotValue(EquipSlotTypes slot, long uid)
		{
			_slots[(int)slot] = uid;
			_dirty = true;
			EventManager.Instance.Publish(new PresetChangeEvent(slot, uid));
			reapplyHero();
		}

		// 활성 Hero 의 장비 Aspect 즉시 갱신 (없으면 다음 스폰 시 ApplyAll 에서 반영)
		private void reapplyHero()
		{
			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				Hero hero = heroes[i] as Hero;
				if (hero != null)
				{
					HeroAspectRegistry.Instance.Reapply(hero, EquipmentSource);
				}
			}
		}
	}
}
