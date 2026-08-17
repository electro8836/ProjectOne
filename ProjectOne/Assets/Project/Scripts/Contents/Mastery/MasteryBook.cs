using System.Collections.Generic;
using EDT;
using ProjectOne.Event;
using ProjectOne.Shared;

namespace ProjectOne.Mastery
{
	// 전 마스터리의 진행도 소유자 (마스터리 설계 8.1).
	//
	// 마스터리는 네 개의 축이 **병렬로** 존재한다. 무기를 바꿔도 이전 마스터리의 진행도는
	// 그 자리에 보존되며, 리셋이라는 개념이 없다 (설계 5.2).
	//
	// 한 번이라도 든 무기만 항목이 생긴다. 없으면 Lv1 / 0포인트로 취급한다.
	public sealed class MasteryBook
	{
		private readonly Dictionary<WeaponMastery, MasteryProgress> _byId =
			new Dictionary<WeaponMastery, MasteryProgress>();

		// 전역 업적 포인트 — 전 마스터리가 각자 전액을 쓴다 (설계 7.1).
		private int _achievementPoint;

		public MasteryBook(MasteryDto dto)
		{
			buildFromDto(dto);
		}

		public int AchievementPoint
		{
			get { return _achievementPoint; }
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 진행도를 얻는다. 없으면 만들어 등록한다(Lv1 / 0포인트 상태).
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

		// 현재 장착 무기의 마스터리. 무기 미착용이면 null (설계 4.3).
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

		// ── 경험치 ────────────────────────────────────────────────────

		// 현재 장착 무기의 마스터리에만 적립한다. 무기 미착용이면 버린다 (설계 4.3 · 5.2).
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
			return progress.Level != before;
		}

		// ── 스킬 트리 ─────────────────────────────────────────────────

		// 노드 투자. 성공하면 스탯·리졸브 캐시가 모두 무효화된다 (설계 11.4).
		public bool TryInvest(WeaponMastery id, int nodeId)
		{
			MasteryProgress progress = GetOrCreate(id);
			if (progress == null || progress.TryInvest(nodeId, _achievementPoint) == false)
			{
				return false;
			}

			notifyChanged(id);
			return true;
		}

		public void ResetTree(WeaponMastery id)
		{
			MasteryProgress progress = Find(id);
			if (progress == null)
			{
				return;
			}

			progress.ResetTree();
			notifyChanged(id);
		}

		// 트리가 바뀐 마스터리가 지금 장착 중일 때만 히어로를 다시 굽는다.
		// 다른 무기의 트리를 손봐도 현재 빌드는 변하지 않는다 (설계 8.3 [3]).
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

		// ── 업적 포인트 ───────────────────────────────────────────────

		// 업적 시스템은 미구현이다. 인터페이스만 열어 둔다 (설계 7.5).
		public void SetAchievementPoint(int value)
		{
			int cap = MasteryCatalog.GetMaxPoint(SkillPoint.SkillPoint_Achievement);
			if (cap > 0 && value > cap)
			{
				value = cap;
			}

			_achievementPoint = value < 0 ? 0 : value;
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public MasteryDto ToDto()
		{
			MasteryDto dto = new MasteryDto();
			dto.achievementPoint = _achievementPoint;

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
			_achievementPoint = 0;

			if (dto == null)
			{
				return;
			}

			SetAchievementPoint(dto.achievementPoint);

			if (dto.masteries == null)
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
			}
		}
	}
}
