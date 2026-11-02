using System.Collections.Generic;
using EDT;
using ProjectOne.Shared;

namespace ProjectOne.Mastery
{
	// 슬롯 조작이 막힌 이유. UI 가 "왜 안 되는지" 를 알려주려면 성패 bool 만으로는 부족하다.
	public enum SlotBlock
	{
		None = 0,		// 가능
		InvalidSlot,	// 슬롯 번호가 범위 밖
		NotInMastery,	// 이 마스터리의 스킬이 아니다
		Locked,			// 아직 해금 레벨에 못 미친다
		WrongSlot,		// 그 칸이 받지 않는 분류다 (패시브 칸·궁극기 칸)
		InDungeon		// 던전 안에서는 바꿀 수 없다 (설계 7.2)
	}

	// 마스터리 1종의 진행도 (마스터리 설계 8.1).
	//
	// 레벨·해금 여부는 **저장하지 않고 계산한다** (설계 8.2).
	// 저장되는 것은 누적 경험치와 슬롯 6칸뿐이다.
	public sealed class MasteryProgress
	{
		public readonly WeaponMastery id;

		private int _totalExp;

		// 스킬 슬롯 6칸 — [0] 패시브 · [1~4] 액티브 · [5] 궁극기. 빈 칸은 Skill.None (설계 7.1).
		private readonly EDT.Skill[] _slots = new EDT.Skill[MasteryRules.SlotCount];

		public MasteryProgress(WeaponMastery id)
		{
			this.id = id;
		}

		public int TotalExp
		{
			get { return _totalExp; }
		}

		// 누적 경험치에서 파생된다. 만렙 초과분은 버린다 (설계 5.1).
		public int Level
		{
			get { return MasteryCatalog.GetMasteryLevel(_totalExp); }
		}

		// ── 경험치 ────────────────────────────────────────────────────

		public void AddExp(int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			_totalExp += amount;
		}

		// 서버 권위값으로 덮는다(던전 클리어·필드보스 정산).
		public void SetExp(int totalExp)
		{
			_totalExp = totalExp > 0 ? totalExp : 0;
		}

		// ── 해금 ──────────────────────────────────────────────────────

		// 해금 레벨에 도달했는가. 이 마스터리의 스킬이 아니면 false.
		public bool IsUnlocked(EDT.Skill skill)
		{
			int unlockLevel = MasteryCatalog.GetUnlockLevel(id, skill);
			return unlockLevel > 0 && Level >= unlockLevel;
		}

		// ── 슬롯 ──────────────────────────────────────────────────────

		public EDT.Skill GetSlot(int index)
		{
			if (index < 0 || index >= _slots.Length)
			{
				return EDT.Skill.None;
			}

			return _slots[index];
		}

		// 스킬이 끼워진 칸. 없으면 -1.
		public int IndexOfSlot(EDT.Skill skill)
		{
			if (skill == EDT.Skill.None)
			{
				return -1;
			}

			for (int i = 0; i < _slots.Length; i++)
			{
				if (_slots[i] == skill)
				{
					return i;
				}
			}

			return -1;
		}

		// 슬롯 조작이 막힌 이유 — 판정의 유일한 출처다. TrySetSlot 과 UI 가 모두 이 함수만 본다.
		// 빈 칸으로 만드는 것(Skill.None)은 해금과 무관하게 허용한다. 던전 판정은 호출자(MasteryBook)가 한다.
		public SlotBlock GetSlotBlock(int index, EDT.Skill skill)
		{
			if (index < 0 || index >= _slots.Length)
			{
				return SlotBlock.InvalidSlot;
			}

			if (skill == EDT.Skill.None)
			{
				return SlotBlock.None;
			}

			int unlockLevel = MasteryCatalog.GetUnlockLevel(id, skill);
			if (unlockLevel <= 0)
			{
				return SlotBlock.NotInMastery;
			}

			if (Level < unlockLevel)
			{
				return SlotBlock.Locked;
			}

			if (MasteryRules.FitsSlot(index, skill) == false)
			{
				return SlotBlock.WrongSlot;
			}

			return SlotBlock.None;
		}

		// 칸에 스킬을 넣는다. 이미 다른 칸에 있던 스킬이면 두 칸을 맞바꾼다 — 같은 스킬이 두 칸에 있으면 안 된다.
		// 값이 그대로면 false(바뀐 것이 없다).
		public bool TrySetSlot(int index, EDT.Skill skill)
		{
			if (GetSlotBlock(index, skill) != SlotBlock.None)
			{
				return false;
			}

			if (_slots[index] == skill)
			{
				return false;
			}

			int existing = IndexOfSlot(skill);
			if (existing >= 0)
			{
				_slots[existing] = _slots[index];
			}

			_slots[index] = skill;
			return true;
		}

		// 새로 해금된 스킬을 그 분류의 앞쪽 빈 칸에 넣는다. 빈 칸이 없거나 이미 끼워져 있으면 false (설계 7.3).
		public bool TryAutoEquip(EDT.Skill skill)
		{
			if (IsUnlocked(skill) == false || IndexOfSlot(skill) >= 0)
			{
				return false;
			}

			for (int i = 0; i < _slots.Length; i++)
			{
				if (_slots[i] == EDT.Skill.None && MasteryRules.FitsSlot(i, skill) == true)
				{
					_slots[i] = skill;
					return true;
				}
			}

			return false;
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		// 저장값을 읽는다. 지금 규칙에 맞지 않는 칸(이 마스터리의 스킬이 아님·칸 분류가 다름)은 비운다 —
		// 스킬 구성이나 칸 배치가 바뀌기 전의 저장값이 그대로 남으면 서버 검증을 영영 통과하지 못한다.
		// 비운 칸이 있으면 true — 호출자가 다시 채우고 저장 대상으로 표시한다.
		public bool LoadFrom(MasteryProgressDto dto)
		{
			for (int i = 0; i < _slots.Length; i++)
			{
				_slots[i] = EDT.Skill.None;
			}

			if (dto == null)
			{
				_totalExp = 0;
				return false;
			}

			_totalExp = dto.totalExp;
			if (dto.slots == null)
			{
				return false;
			}

			bool cleared = dto.slots.Count != _slots.Length && dto.slots.Count > 0;
			int count = dto.slots.Count < _slots.Length ? dto.slots.Count : _slots.Length;
			for (int i = 0; i < count; i++)
			{
				EDT.Skill skill = (EDT.Skill)dto.slots[i];
				if (skill == EDT.Skill.None)
				{
					continue;
				}

				if (MasteryCatalog.GetUnlockLevel(id, skill) <= 0 || MasteryRules.FitsSlot(i, skill) == false)
				{
					cleared = true;
					continue;
				}

				_slots[i] = skill;
			}

			return cleared;
		}

		public MasteryProgressDto ToDto()
		{
			MasteryProgressDto dto = new MasteryProgressDto();
			dto.masteryId = (int)id;
			dto.level = Level;
			dto.totalExp = _totalExp;

			for (int i = 0; i < _slots.Length; i++)
			{
				dto.slots.Add((int)_slots[i]);
			}

			return dto;
		}
	}
}
