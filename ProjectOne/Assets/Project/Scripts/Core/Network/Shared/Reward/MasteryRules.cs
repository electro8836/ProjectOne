using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 마스터리 규칙 중 테이블만 보고 판단하는 부분 (마스터리 설계 5·6·7장). 클라·서버 공용.
	//
	// 클라는 레벨 계산과 해금 판정을 여기에 위임하고, 서버는 같은 규칙으로 저장 요청(슬롯 최종 상태)을 검증한다.
	// 클라의 슬롯 조작(MasteryProgress.TrySetSlot)은 한 번의 조작을 막는 규칙이고,
	// ValidateSlots 는 그 조작들의 **결과 상태**가 유효한지 보는 규칙이다 — 같은 불변식의 양면이다.
	public static class MasteryRules
	{
		// 마스터리마다 스킬 슬롯 6칸 (설계 7.1). 평타는 슬롯 밖이다.
		// [0] 패시브 · [1~4] 액티브 · [5] 궁극기 — 칸마다 받는 스킬 분류가 정해져 있다.
		public const int SlotCount = 6;
		public const int PassiveSlot = 0;
		public const int UltimateSlot = 5;

		// 그 칸이 받는 스킬 분류. 범위 밖이면 None.
		public static SkillCategoryTypes GetSlotCategory(int index)
		{
			if (index < 0 || index >= SlotCount)
			{
				return SkillCategoryTypes.None;
			}

			if (index == PassiveSlot)
			{
				return SkillCategoryTypes.Passive;
			}

			if (index == UltimateSlot)
			{
				return SkillCategoryTypes.Ultimate;
			}

			return SkillCategoryTypes.Active;
		}

		// 스킬이 그 칸에 들어갈 수 있는 분류인가.
		public static bool FitsSlot(int index, EDT.Skill skill)
		{
			Table_Skill.Row row = Table_Skill.Get(skill);
			return row != null && row.SkillCategory == GetSlotCategory(index);
		}

		// 레벨 곡선 — ID(레벨) 오름차순. 테이블 로드 후 처음 쓸 때 만든다.
		private static List<Table_MasteryLevelExp.Row> _curve;

		// 누적 경험치로 도달 레벨을 구한다 (설계 5.1 — 누적값이라 이분 탐색 1회면 된다). 만렙 초과분은 버린다.
		public static int LevelFromExp(int totalExp)
		{
			List<Table_MasteryLevelExp.Row> curve = getCurve();
			if (curve.Count == 0)
			{
				return 1;
			}

			int lo = 0;
			int hi = curve.Count - 1;
			int result = curve[0].ID;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (curve[mid].TotalExperience <= totalExp)
				{
					result = curve[mid].ID;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}

			return result;
		}

		// 그 마스터리에서 스킬이 해금되는 레벨. 그 마스터리의 스킬이 아니면 0 (설계 6.2).
		public static int GetUnlockLevel(WeaponMastery id, EDT.Skill skill)
		{
			if (id == WeaponMastery.None || skill == EDT.Skill.None)
			{
				return 0;
			}

			Dictionary<int, Table_MasterySkill.Row>.Enumerator e = Table_MasterySkill.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MasterySkill.Row row = e.Current.Value;
				if (row.WeaponMastery == id && row.SkillID == skill)
				{
					return row.UnlockLevel;
				}
			}

			return 0;
		}

		// 슬롯 최종 상태 검증 (설계 7.4). 유효하면 null, 아니면 사유.
		// 빈 칸(0)은 허용한다. 해금은 **서버의** 누적 경험치로 판정한다.
		public static string ValidateSlots(WeaponMastery id, IReadOnlyList<int> slots, int totalExp)
		{
			if (id == WeaponMastery.None || Table_WeaponMastery.Get(id) == null)
			{
				return "unknown mastery " + (int)id;
			}

			if (slots == null)
			{
				return "slots missing";
			}

			if (slots.Count > SlotCount)
			{
				return "too many slots " + slots.Count;
			}

			int level = LevelFromExp(totalExp);
			HashSet<int> seen = new HashSet<int>();
			for (int i = 0; i < slots.Count; i++)
			{
				int skillId = slots[i];
				if (skillId == 0)
				{
					continue;
				}

				int unlockLevel = GetUnlockLevel(id, (EDT.Skill)skillId);
				if (unlockLevel <= 0)
				{
					return "skill " + skillId + " not in mastery";
				}

				if (level < unlockLevel)
				{
					return "skill " + skillId + " locked: " + level + " < " + unlockLevel;
				}

				if (FitsSlot(i, (EDT.Skill)skillId) == false)
				{
					return "skill " + skillId + " wrong slot " + i;
				}

				if (seen.Add(skillId) == false)
				{
					return "duplicated skill " + skillId;
				}
			}

			return null;
		}

		// 서버 적립 — 해당 마스터리 진행도에 경험치를 더한다. 처음 든 무기면 항목을 만든다.
		public static void AddExp(MasteryDto dto, WeaponMastery id, int amount)
		{
			if (dto == null || id == WeaponMastery.None || amount <= 0)
			{
				return;
			}

			MasteryProgressDto progress = FindOrCreate(dto, id);
			progress.totalExp += amount;
			progress.level = LevelFromExp(progress.totalExp);
		}

		public static MasteryProgressDto FindOrCreate(MasteryDto dto, WeaponMastery id)
		{
			for (int i = 0; i < dto.masteries.Count; i++)
			{
				if (dto.masteries[i] != null && dto.masteries[i].masteryId == (int)id)
				{
					return dto.masteries[i];
				}
			}

			MasteryProgressDto created = new MasteryProgressDto();
			created.masteryId = (int)id;
			dto.masteries.Add(created);
			return created;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static List<Table_MasteryLevelExp.Row> getCurve()
		{
			if (_curve != null && _curve.Count > 0)
			{
				return _curve;
			}

			List<Table_MasteryLevelExp.Row> curve = new List<Table_MasteryLevelExp.Row>(Table_MasteryLevelExp.All().Values);
			curve.Sort(compareLevel);
			_curve = curve;
			return _curve;
		}

		private static int compareLevel(Table_MasteryLevelExp.Row a, Table_MasteryLevelExp.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
