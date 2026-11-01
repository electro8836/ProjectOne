using System.Collections.Generic;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Shared;

namespace ProjectOne.Mastery
{
	// 전 마스터리의 진행도 소유자 (마스터리 설계 8.1).
	//
	// 마스터리는 네 개의 축이 **병렬로** 존재한다. 무기를 바꿔도 이전 마스터리의 진행도는
	// 그 자리에 보존되며, 리셋이라는 개념이 없다 (설계 5.2).
	//
	// 한 번이라도 든 무기만 항목이 생긴다. 없으면 Lv1 / 빈 슬롯으로 취급한다.
	public sealed class MasteryBook
	{
		private readonly Dictionary<WeaponMastery, MasteryProgress> _byId =
			new Dictionary<WeaponMastery, MasteryProgress>();

		// 서버에 아직 저장하지 않은 슬롯 변경이 있는 마스터리.
		// 조작은 로컬에 즉시 반영하고, 화면 닫기·일시정지·종료 때 묶어서 보낸다(SaveMasterySlots).
		private readonly HashSet<WeaponMastery> _dirtySlots = new HashSet<WeaponMastery>();

		public MasteryBook(MasteryDto dto)
		{
			buildFromDto(dto);
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 진행도를 얻는다. 없으면 만들어 등록한다(Lv1 / 빈 슬롯 상태).
		public MasteryProgress GetOrCreate(WeaponMastery id)
		{
			if (id == WeaponMastery.None)
			{
				return null;
			}

			MasteryProgress progress;
			if (_byId.TryGetValue(id, out progress) == false)
			{
				progress = new MasteryProgress(id);
				_byId.Add(id, progress);

				// 처음 든 무기 — 1레벨 스킬이 바로 해금되므로 빈 칸에 넣는다 (설계 6.3 · 7.3).
				fillUnlocked(progress);
			}

			return progress;
		}

		// 진행도를 읽기만 한다. 없으면 null — 조회 때문에 항목이 생기는 것을 막는다.
		public MasteryProgress Find(WeaponMastery id)
		{
			MasteryProgress progress;
			_byId.TryGetValue(id, out progress);
			return progress;
		}

		// 마스터리 레벨. 한 번도 들지 않은 무기는 항목이 없지만 GetOrCreate 가 "없으면 Lv1" 로 정의하므로
		// 읽는 쪽도 같은 축을 쓴다. 조회 때문에 항목이 생기지 않도록 Find 로 읽는다.
		public int GetLevel(WeaponMastery id)
		{
			MasteryProgress progress = Find(id);
			return (progress != null) ? progress.Level : 1;
		}

		// 전 마스터리의 레벨 합. 총 레벨 보너스(Table_MasteryLevelBonus)가 이 값에 곱해진다.
		public int TotalLevel
		{
			get
			{
				int sum = 0;
				Dictionary<WeaponMastery, Table_WeaponMastery.Row> all = MasteryCatalog.All();
				Dictionary<WeaponMastery, Table_WeaponMastery.Row>.Enumerator e = all.GetEnumerator();
				while (e.MoveNext() == true)
				{
					sum += GetLevel(e.Current.Key);
				}

				return sum;
			}
		}

		// 현재 장착 무기의 마스터리. 무기 미착용이면 null (설계 4.4).
		public Table_WeaponMastery.Row CurrentMastery
		{
			get
			{
				WeaponType weaponType = UserData.Account.Instance.Loadout.EquippedWeaponType;
				return MasteryCatalog.GetByWeaponType(weaponType);
			}
		}

		public MasteryProgress CurrentProgress
		{
			get
			{
				Table_WeaponMastery.Row row = CurrentMastery;
				return row != null ? GetOrCreate(row.ID) : null;
			}
		}

		// 대표 레벨 — 장착 무기의 마스터리 레벨. 무기 미착용이면 0 (설계 3.3).
		public int RepresentativeLevel
		{
			get
			{
				Table_WeaponMastery.Row row = CurrentMastery;
				return row != null ? GetLevel(row.ID) : 0;
			}
		}

		// ── 경험치 ────────────────────────────────────────────────────

		// 현재 장착 무기의 마스터리에만 적립한다. 무기 미착용이면 버린다 (설계 4.4 · 5.2).
		// 레벨업 여부를 반환해 호출자가 캐시 무효화를 판단할 수 있게 한다.
		public bool AddExpToCurrent(int amount)
		{
			if (amount <= 0)
			{
				return false;
			}

			MasteryProgress progress = CurrentProgress;
			if (progress == null)
			{
				return false;
			}

			int before = progress.Level;
			progress.AddExp(amount);
			return onExpChanged(progress, before);
		}

		// 서버 권위값으로 덮는다 — 마스터리마다 서버 누적 경험치 + 아직 서버에 올라가지 않은 처치 경험치.
		// 레벨이 바뀐 마스터리가 하나라도 있으면 true.
		public bool ApplyAuthoritative(MasteryDto server, IReadOnlyDictionary<WeaponMastery, int> unsettledExp)
		{
			if (server == null || server.masteries == null)
			{
				return false;
			}

			bool leveled = false;
			for (int i = 0; i < server.masteries.Count; i++)
			{
				MasteryProgressDto src = server.masteries[i];
				if (src == null)
				{
					continue;
				}

				MasteryProgress progress = GetOrCreate((WeaponMastery)src.masteryId);
				if (progress == null)
				{
					continue;
				}

				int pending;
				if (unsettledExp == null || unsettledExp.TryGetValue(progress.id, out pending) == false)
				{
					pending = 0;
				}

				int before = progress.Level;
				progress.SetExp(src.totalExp + pending);
				if (onExpChanged(progress, before) == true)
				{
					leveled = true;
				}
			}

			return leveled;
		}

		// 레벨이 오르면 새로 해금된 스킬을 빈 칸에 넣고 알린다 (설계 7.3). 레벨이 바뀌었으면 true.
		private bool onExpChanged(MasteryProgress progress, int before)
		{
			EventManager.Instance.Publish(new MasteryExpChangeEvent(progress.id));

			int after = progress.Level;
			if (after == before)
			{
				return false;
			}

			if (after > before)
			{
				bool equipped = false;
				IReadOnlyList<Table_MasterySkill.Row> skills = MasteryCatalog.GetSkills(progress.id);
				for (int i = 0; i < skills.Count; i++)
				{
					int unlockLevel = skills[i].UnlockLevel;
					if (unlockLevel <= before || unlockLevel > after)
					{
						continue;
					}

					if (progress.TryAutoEquip(skills[i].SkillID) == true)
					{
						equipped = true;
					}
				}

				if (equipped == true)
				{
					_dirtySlots.Add(progress.id);
					EventManager.Instance.Publish(new MasteryChangeEvent(progress.id));
				}

				EventManager.Instance.Publish(new MasteryLevelUpEvent(progress.id, after));
			}

			return true;
		}

		// 이미 해금된 스킬을 해금 순서대로 빈 칸에 넣는다. 하나라도 넣었으면 저장 대상으로 표시한다.
		private void fillUnlocked(MasteryProgress progress)
		{
			bool equipped = false;
			IReadOnlyList<Table_MasterySkill.Row> skills = MasteryCatalog.GetSkills(progress.id);
			for (int i = 0; i < skills.Count; i++)
			{
				if (progress.TryAutoEquip(skills[i].SkillID) == true)
				{
					equipped = true;
				}
			}

			if (equipped == true)
			{
				_dirtySlots.Add(progress.id);
			}
		}

		// ── 스킬 슬롯 ─────────────────────────────────────────────────

		// 던전 안에서는 슬롯을 바꿀 수 없다 (설계 7.2).
		public static bool IsSlotLocked
		{
			get { return DungeonDirector.HasInstance == true; }
		}

		// 슬롯 조작이 막힌 이유. 조회 때문에 항목이 생기지 않도록 Find 로 읽는다.
		public SlotBlock GetSlotBlock(WeaponMastery id, int index, EDT.Skill skill)
		{
			if (IsSlotLocked == true)
			{
				return SlotBlock.InDungeon;
			}

			MasteryProgress progress = Find(id);
			if (progress == null)
			{
				progress = new MasteryProgress(id);
			}

			return progress.GetSlotBlock(index, skill);
		}

		// 칸에 스킬을 넣는다(Skill.None 이면 비운다). 다른 칸에 있던 스킬이면 두 칸을 맞바꾼다.
		public bool TrySetSlot(WeaponMastery id, int index, EDT.Skill skill)
		{
			if (IsSlotLocked == true)
			{
				return false;
			}

			MasteryProgress progress = GetOrCreate(id);
			if (progress == null || progress.TrySetSlot(index, skill) == false)
			{
				return false;
			}

			_dirtySlots.Add(id);
			notifyChanged(id);
			return true;
		}

		// 슬롯이 바뀐 마스터리가 지금 장착 중일 때만 히어로를 다시 굽는다.
		// 다른 무기의 슬롯을 손봐도 현재 빌드는 변하지 않는다 (설계 7.4).
		private void notifyChanged(WeaponMastery id)
		{
			EventManager.Instance.Publish(new MasteryChangeEvent(id));

			Table_WeaponMastery.Row current = CurrentMastery;
			if (current == null || current.ID != id)
			{
				return;
			}

			UserData.Account.Instance.Loadout.ReapplyMastery();
		}

		// ── 서버 저장 ─────────────────────────────────────────────────

		public bool IsSlotDirty
		{
			get { return _dirtySlots.Count > 0; }
		}

		// 바뀐 슬롯의 현재 상태를 꺼내고 표시를 지운다. 전송 중에 또 바뀌면 다시 표시되어 다음 저장에 실린다.
		public MasteryProgressDto[] TakeDirtySlots()
		{
			List<MasteryProgressDto> masteries = new List<MasteryProgressDto>(_dirtySlots.Count);
			HashSet<WeaponMastery>.Enumerator e = _dirtySlots.GetEnumerator();
			while (e.MoveNext() == true)
			{
				MasteryProgress progress = Find(e.Current);
				if (progress != null)
				{
					masteries.Add(progress.ToDto());
				}
			}

			_dirtySlots.Clear();
			return masteries.ToArray();
		}

		// 저장 실패 — 꺼냈던 마스터리를 다시 표시해 다음 트리거에 재전송한다.
		public void RestoreDirtySlots(MasteryProgressDto[] masteries)
		{
			if (masteries == null)
			{
				return;
			}

			for (int i = 0; i < masteries.Length; i++)
			{
				_dirtySlots.Add((WeaponMastery)masteries[i].masteryId);
			}
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public MasteryDto ToDto()
		{
			MasteryDto dto = new MasteryDto();

			Dictionary<WeaponMastery, MasteryProgress>.Enumerator e = _byId.GetEnumerator();
			while (e.MoveNext() == true)
			{
				dto.masteries.Add(e.Current.Value.ToDto());
			}

			return dto;
		}

		private void buildFromDto(MasteryDto dto)
		{
			_byId.Clear();

			if (dto == null || dto.masteries == null)
			{
				return;
			}

			for (int i = 0; i < dto.masteries.Count; i++)
			{
				MasteryProgressDto src = dto.masteries[i];
				if (src == null)
				{
					continue;
				}

				WeaponMastery id = (WeaponMastery)src.masteryId;
				if (id == WeaponMastery.None)
				{
					continue;
				}

				MasteryProgress progress = new MasteryProgress(id);
				progress.LoadFrom(src);
				_byId[id] = progress;

				// 슬롯을 한 번도 저장한 적 없는 진행도 — 이미 해금된 스킬로 채운다.
				// 일부러 비운 슬롯은 빈 칸으로 저장되므로(slots 4칸) 여기 걸리지 않는다.
				if (src.slots == null || src.slots.Count == 0)
				{
					fillUnlocked(progress);
				}
			}
		}
	}
}
