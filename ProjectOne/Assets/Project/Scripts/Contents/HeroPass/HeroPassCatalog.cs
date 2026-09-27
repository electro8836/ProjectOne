using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.HeroPasses
{
	// 히어로패스 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// HeroPass(레벨별 보상) 는 ID 가 곧 레벨이고, HeroPassLevelExp.TotalExp 는 그 레벨에 도달하는 **누적** 경험치다.
	// 경험치 획득 규칙(HeroPassExpInfo) 은 유형별 목록으로 굽는다 — 처치마다 전체를 훑지 않도록.
	//
	// DailyBonusCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class HeroPassCatalog
	{
		// 레벨 오름차순. 인덱스 0 이 1레벨이다.
		private static readonly List<Table_HeroPass.Row> _levels = new List<Table_HeroPass.Row>();

		// 인덱스 = 레벨. 0레벨은 0 이다.
		private static readonly List<int> _totalExps = new List<int>();

		private static readonly Dictionary<HeroPassExpType, List<Table_HeroPassExpInfo.Row>> _expInfos = new Dictionary<HeroPassExpType, List<Table_HeroPassExpInfo.Row>>();

		private static readonly List<Table_HeroPassExpInfo.Row> _emptyExpInfos = new List<Table_HeroPassExpInfo.Row>();

		public static int MaxLevel
		{
			get { return _levels.Count; }
		}

		public static void Build()
		{
			buildLevels();
			buildExpInfos();

			Debug.Log($"[HeroPassCatalog] 구축 완료 — 레벨:{_levels.Count} 획득규칙:{Table_HeroPassExpInfo.All().Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static IReadOnlyList<Table_HeroPass.Row> GetLevels()
		{
			return _levels;
		}

		// level 에 도달하는 누적 경험치. 0레벨은 0, 범위 밖은 끝값으로 자른다.
		public static int GetTotalExp(int level)
		{
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
			for (int i = 1; i <= MaxLevel; i++)
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
			List<Table_HeroPassExpInfo.Row> list;
			if (_expInfos.TryGetValue(type, out list) == true)
			{
				return list;
			}

			return _emptyExpInfos;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void buildLevels()
		{
			_levels.Clear();
			_totalExps.Clear();

			Dictionary<int, Table_HeroPass.Row>.Enumerator e = Table_HeroPass.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				_levels.Add(e.Current.Value);
			}

			_levels.Sort(compareById);

			// 보상이 있는 레벨까지만 경험치를 둔다 — 보상 없는 레벨은 도달해도 받을 것이 없다.
			_totalExps.Add(0);
			for (int i = 0; i < _levels.Count; i++)
			{
				Table_HeroPassLevelExp.Row expRow = Table_HeroPassLevelExp.Get(_levels[i].ID);
				_totalExps.Add((expRow != null) ? expRow.TotalExp : 0);
			}
		}

		private static void buildExpInfos()
		{
			_expInfos.Clear();

			Dictionary<int, Table_HeroPassExpInfo.Row>.Enumerator e = Table_HeroPassExpInfo.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_HeroPassExpInfo.Row row = e.Current.Value;
				if (row.ExpType == HeroPassExpType.None)
				{
					Debug.LogWarning($"[HeroPassCatalog] HeroPassExpInfo ID {row.ID} 의 ExpType 이 비어 있다 — 건너뛴다.");
					continue;
				}

				if (row.ReqCount <= 0)
				{
					Debug.LogWarning($"[HeroPassCatalog] HeroPassExpInfo ID {row.ID} 의 ReqCount 가 {row.ReqCount} 다 — 건너뛴다.");
					continue;
				}

				List<Table_HeroPassExpInfo.Row> list;
				if (_expInfos.TryGetValue(row.ExpType, out list) == false)
				{
					list = new List<Table_HeroPassExpInfo.Row>();
					_expInfos.Add(row.ExpType, list);
				}

				list.Add(row);
			}
		}

		private static int compareById(Table_HeroPass.Row a, Table_HeroPass.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		// 레벨이 1부터 이어지는지, 누적 경험치가 늘어나는지, 보상 그룹이 비어 있지 않은지 본다.
		// 보상 누락은 한 줄로 묶는다(DailyBonusCatalog 와 같은 이유).
		private static void validate()
		{
			System.Text.StringBuilder missing = new System.Text.StringBuilder();

			for (int i = 0; i < _levels.Count; i++)
			{
				Table_HeroPass.Row row = _levels[i];
				int level = i + 1;

				if (row.ID != level)
				{
					Debug.LogWarning($"[HeroPassCatalog] 레벨이 이어지지 않는다 — {level} 이어야 할 자리에 {row.ID} 가 있다.");
				}

				if (_totalExps[level] <= _totalExps[level - 1])
				{
					Debug.LogWarning($"[HeroPassCatalog] {level}레벨 TotalExp({_totalExps[level]}) 가 이전 레벨보다 크지 않다 — HeroPassLevelExp 를 확인한다.");
				}

				if (row.RewardGroupID_Normal <= 0 || row.RewardGroupID_Advanced <= 0)
				{
					if (missing.Length > 0)
					{
						missing.Append(", ");
					}

					missing.Append(level);
				}
			}

			if (missing.Length > 0)
			{
				Debug.LogWarning($"[HeroPassCatalog] RewardGroupID 가 비어 있다 — {missing}레벨. HeroPass 엑셀을 채워야 한다.");
			}
		}
	}
}
