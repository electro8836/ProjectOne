using System;
using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 히어로패스 규칙 — 레벨·누적 경험치·획득 규칙·시즌·수령 판정. 클라·서버 공용.
	//
	// HeroPass(레벨별 보상) 는 ID 가 곧 레벨이고, HeroPassLevelExp.TotalExp 는 그 레벨에 도달하는 **누적** 경험치다.
	// 경험치는 활동 유형별 카운터가 HeroPassExpInfo.ReqCount 의 배수에 닿을 때마다 ExpAmount 씩 쌓인다.
	//
	// 서버는 처치·던전 클리어를 받을 때 같은 규칙으로 세고(권위), 클라는 표시용으로 로컬에서도 센다.
	public static class HeroPassRules
	{
		public const int SeasonDays = 35;

		private static readonly List<Table_HeroPassExpInfo.Row> _emptyExpInfos = new List<Table_HeroPassExpInfo.Row>();

		// 레벨 오름차순. 인덱스 0 이 1레벨이다.
		private static List<Table_HeroPass.Row> _levels;

		// 인덱스 = 레벨. 0레벨은 0 이다.
		private static List<int> _totalExps;

		private static Dictionary<HeroPassExpType, List<Table_HeroPassExpInfo.Row>> _expInfos;

		// ── 테이블 조회 ───────────────────────────────────────────────

		public static int MaxLevel
		{
			get
			{
				ensureBuilt();
				return _levels.Count;
			}
		}

		public static IReadOnlyList<Table_HeroPass.Row> GetLevels()
		{
			ensureBuilt();
			return _levels;
		}

		// level 에 도달하는 누적 경험치. 0레벨은 0, 범위 밖은 끝값으로 자른다.
		public static int GetTotalExp(int level)
		{
			ensureBuilt();

			if (level <= 0 || _totalExps.Count == 0)
			{
				return 0;
			}

			if (level >= _totalExps.Count)
			{
				return _totalExps[_totalExps.Count - 1];
			}

			return _totalExps[level];
		}

		// 최대 레벨 도달에 필요한 누적 경험치 — 이 이상은 버린다.
		public static int GetMaxExp()
		{
			return GetTotalExp(MaxLevel);
		}

		// 누적 경험치로 도달한 레벨. 아무것도 못 넘었으면 0.
		public static int GetLevelByExp(int exp)
		{
			int level = 0;
			int maxLevel = MaxLevel;
			for (int i = 1; i <= maxLevel; i++)
			{
				if (exp < GetTotalExp(i))
				{
					break;
				}

				level = i;
			}

			return level;
		}

		public static IReadOnlyList<Table_HeroPassExpInfo.Row> GetExpInfos(HeroPassExpType type)
		{
			ensureBuilt();

			List<Table_HeroPassExpInfo.Row> list;
			if (_expInfos.TryGetValue(type, out list) == true)
			{
				return list;
			}

			return _emptyExpInfos;
		}

		// 처치 몬스터 등급 → 활동 유형. 해당 없으면 None.
		public static HeroPassExpType ToKillType(MonsterType monsterType)
		{
			switch (monsterType)
			{
				case MonsterType.Normal:
					return HeroPassExpType.MonsterKill_Normal;

				case MonsterType.Elite:
					return HeroPassExpType.MonsterKill_Elite;

				case MonsterType.Boss:
					return HeroPassExpType.MonsterKill_Boss;
			}

			return HeroPassExpType.None;
		}

		// ── 진행도 ────────────────────────────────────────────────────

		// 시즌 경계를 넘었으면 경험치·카운터·수령 기록·구매 여부를 전부 초기화한다. 모든 판정·변경 전에 부른다.
		public static void EnsureSeason(HeroPassDto dto, int today)
		{
			int season = getSeasonIndex(dto, today);
			if (season == dto.seasonIndex)
			{
				return;
			}

			dto.seasonIndex = season;
			dto.exp = 0;
			dto.purchased = false;
			dto.counters.Clear();
			dto.claimedNormal.Clear();
			dto.claimedAdvanced.Clear();
		}

		// 활동 1회를 센다. ReqCount 배수에 닿은 규칙마다 ExpAmount 를 적립하고, 이번에 늘어난 경험치를 돌려준다.
		public static int AddCount(HeroPassDto dto, HeroPassExpType type)
		{
			if (type == HeroPassExpType.None)
			{
				return 0;
			}

			HeroPassCounterDto counter = getOrCreateCounter(dto, (int)type);
			counter.count++;

			int gained = 0;
			IReadOnlyList<Table_HeroPassExpInfo.Row> infos = GetExpInfos(type);
			for (int i = 0; i < infos.Count; i++)
			{
				if (counter.count % infos[i].ReqCount == 0)
				{
					gained += infos[i].ExpAmount;
				}
			}

			if (gained <= 0)
			{
				return 0;
			}

			// 최대 레벨을 넘는 몫은 버린다.
			int before = dto.exp;
			dto.exp = Math.Min(dto.exp + gained, GetMaxExp());
			return dto.exp - before;
		}

		public static bool IsClaimed(HeroPassDto dto, int level, bool advanced)
		{
			return getClaimed(dto, advanced).Contains(level);
		}

		// 도달했고, 아직 안 받았고, 추가 보상이면 구매까지 했는가.
		public static bool CanClaim(HeroPassDto dto, int level, bool advanced)
		{
			if (level <= 0 || level > GetLevelByExp(dto.exp))
			{
				return false;
			}

			if (advanced == true && dto.purchased == false)
			{
				return false;
			}

			return getClaimed(dto, advanced).Contains(level) == false;
		}

		public static void MarkClaimed(HeroPassDto dto, int level, bool advanced)
		{
			List<int> claimed = getClaimed(dto, advanced);
			if (claimed.Contains(level) == false)
			{
				claimed.Add(level);
			}
		}

		// 이번 시즌의 남은 날수(오늘 포함).
		public static int GetSeasonDaysLeft(HeroPassDto dto, int today)
		{
			int elapsedDays = today - dto.startResetDay;
			if (elapsedDays < 0)
			{
				elapsedDays = 0;
			}

			return SeasonDays - (elapsedDays % SeasonDays);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static int getSeasonIndex(HeroPassDto dto, int today)
		{
			int elapsedDays = today - dto.startResetDay;
			if (elapsedDays < 0)
			{
				return 0;
			}

			return elapsedDays / SeasonDays;
		}

		private static List<int> getClaimed(HeroPassDto dto, bool advanced)
		{
			return advanced ? dto.claimedAdvanced : dto.claimedNormal;
		}

		private static HeroPassCounterDto getOrCreateCounter(HeroPassDto dto, int typeId)
		{
			for (int i = 0; i < dto.counters.Count; i++)
			{
				if (dto.counters[i] != null && dto.counters[i].typeId == typeId)
				{
					return dto.counters[i];
				}
			}

			HeroPassCounterDto created = new HeroPassCounterDto();
			created.typeId = typeId;
			dto.counters.Add(created);
			return created;
		}

		// 테이블 로드 후 처음 쓸 때 만든다. 획득 규칙 인덱스를 마지막에 넣어 완료 표시를 겸한다.
		private static void ensureBuilt()
		{
			if (_expInfos != null)
			{
				return;
			}

			List<Table_HeroPass.Row> levels = new List<Table_HeroPass.Row>();
			Dictionary<int, Table_HeroPass.Row>.Enumerator le = Table_HeroPass.All().GetEnumerator();
			while (le.MoveNext() == true)
			{
				levels.Add(le.Current.Value);
			}

			levels.Sort(compareById);

			// 보상이 있는 레벨까지만 경험치를 둔다 — 보상 없는 레벨은 도달해도 받을 것이 없다.
			List<int> totalExps = new List<int>();
			totalExps.Add(0);
			for (int i = 0; i < levels.Count; i++)
			{
				Table_HeroPassLevelExp.Row expRow = Table_HeroPassLevelExp.Get(levels[i].ID);
				totalExps.Add((expRow != null) ? expRow.TotalExp : 0);
			}

			Dictionary<HeroPassExpType, List<Table_HeroPassExpInfo.Row>> expInfos = new Dictionary<HeroPassExpType, List<Table_HeroPassExpInfo.Row>>();
			Dictionary<int, Table_HeroPassExpInfo.Row>.Enumerator ee = Table_HeroPassExpInfo.All().GetEnumerator();
			while (ee.MoveNext() == true)
			{
				Table_HeroPassExpInfo.Row row = ee.Current.Value;
				if (row.ExpType == HeroPassExpType.None || row.ReqCount <= 0)
				{
					continue;
				}

				List<Table_HeroPassExpInfo.Row> list;
				if (expInfos.TryGetValue(row.ExpType, out list) == false)
				{
					list = new List<Table_HeroPassExpInfo.Row>();
					expInfos.Add(row.ExpType, list);
				}

				list.Add(row);
			}

			_levels = levels;
			_totalExps = totalExps;
			_expInfos = expInfos;
		}

		private static int compareById(Table_HeroPass.Row a, Table_HeroPass.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
