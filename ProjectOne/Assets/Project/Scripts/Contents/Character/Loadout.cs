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
		// EquipmentAspect / MasteryAspect 와 동일한 source 태그
		private const string EquipmentSource = "Equipment";
		private const string MasterySource = "Mastery";

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

		// 마스터리(트리 투자·초기화)가 바뀌었을 때 활성 Hero 를 다시 굽는다.
		// 히어로 참조를 들고 있는 곳이 여기뿐이라 진입점을 함께 둔다.
		public void ReapplyMastery()
		{
			reapplyHero();
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

		// 활성 Hero 의 Aspect 즉시 갱신 (없으면 다음 스폰 시 ApplyAll 에서 반영).
		//
		// 무기를 바꾸면 마스터리가 통째로 교체되므로 장비 Aspect 만으로는 부족하다 —
		// 평타·트리·근접성향이 전부 마스터리 소유다 (마스터리 설계 4.2).
		// 리졸브 캐시도 함께 버린다 (스킬 설계 11.4 — 무기 교체는 가장 무거운 무효화 이벤트다).
		private void reapplyHero()
		{
			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				Hero hero = heroes[i] as Hero;
				if (hero == null)
				{
					continue;
				}

				hero.SkillResolver.Invalidate();
				HeroAspectRegistry.Instance.Reapply(hero, EquipmentSource);
				HeroAspectRegistry.Instance.Reapply(hero, MasterySource);

				// Mastery 출처가 아닌 스킬(장비 SkillGrant 등)의 사이클 값도 다시 읽어야 한다.
				if (hero.SkillContainer != null)
				{
					hero.SkillContainer.RefreshFromResolve();
				}

				// 무기가 바뀌면 공속·이속 스탯도 바뀐다 — 애니 배속을 다시 밀어 넣는다.
				hero.RefreshAnimationStats();
			}
		}
	}
}
