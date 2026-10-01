using System;
using System.Collections.Generic;
using System.Globalization;
using EDT;

namespace ProjectOne.Shared
{
	// 보상 테이블 인덱스 — Reward / RewardItemPool 을 그룹·풀 단위로 미리 굽는다. 클라·서버 공용.
	//
	// RewardItemPool 은 열거식이 아니라 **조건형**이다 — 신규 아이템에 DropTier 만 적으면
	// 자동으로 드랍 풀에 편입되고 보상 테이블은 손댈 필요가 없다 (설계 4.1).
	// 그래서 조건 → 후보 목록을 Build 시점에 한 번 굽는다 (설계 6.2).
	//
	// 정합성 검증·경고 로그는 클라 RewardCatalog 가 맡는다 — 여기는 로그 없이 인덱싱만 한다.
	public static class RewardTable
	{
		// 조건 행 하나와 그 조건에 매칭되는 아이템 후보. Build 시점에 확정된다.
		public sealed class PoolEntry
		{
			public Table_RewardItemPool.Row row;
			public readonly List<int> candidates = new List<int>();
		}

		// Reward.TargetID 는 string 이라 RewardType 별로 해석이 다르다. 그 결과를 미리 굽는다.
		public sealed class RewardEntry
		{
			public Table_Reward.Row row;

			public int itemId;				// RewardType.Item
			public int poolId;				// RewardType.ItemPool
			public EDT.Currency currency;	// RewardType.Currency

			// 해석에 실패한 행은 지급 대상에서 제외한다.
			public bool isValid;
		}

		private static readonly Dictionary<int, List<RewardEntry>> _byGroup = new Dictionary<int, List<RewardEntry>>();
		private static readonly Dictionary<int, List<PoolEntry>> _byPool = new Dictionary<int, List<PoolEntry>>();

		private static readonly List<RewardEntry> _emptyRewards = new List<RewardEntry>();
		private static readonly List<PoolEntry> _emptyPools = new List<PoolEntry>();

		public static int GroupCount
		{
			get { return _byGroup.Count; }
		}

		public static int PoolCount
		{
			get { return _byPool.Count; }
		}

		// 테이블 로드 직후 1회 호출한다.
		public static void Build()
		{
			_byGroup.Clear();
			_byPool.Clear();

			buildPools();
			buildRewards();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 보상 그룹 하나. 소비처가 이 목록을 통째로 굴린다.
		public static IReadOnlyList<RewardEntry> GetGroup(int groupId)
		{
			List<RewardEntry> list;
			if (_byGroup.TryGetValue(groupId, out list) == true)
			{
				return list;
			}

			return _emptyRewards;
		}

		public static IReadOnlyList<PoolEntry> GetPool(int poolId)
		{
			List<PoolEntry> list;
			if (_byPool.TryGetValue(poolId, out list) == true)
			{
				return list;
			}

			return _emptyPools;
		}

		// 전 그룹 순회 — 클라 정합성 검증용.
		public static Dictionary<int, List<RewardEntry>>.Enumerator GetGroupEnumerator()
		{
			return _byGroup.GetEnumerator();
		}

		// 전 풀 순회 — 클라 정합성 검증용.
		public static Dictionary<int, List<PoolEntry>>.Enumerator GetPoolEnumerator()
		{
			return _byPool.GetEnumerator();
		}

		// ── 내부: 인덱싱 ──────────────────────────────────────────────

		// 조건 행마다 후보를 미리 확정한다.
		//
		// SubCategory 가 None 이면 "해당 MainCategory 전체" 와일드카드다. 캐시 키를
		// (DropTier, Main, Sub) 로 두면 이 와일드카드를 조회 시점에 매번 풀어야 하므로,
		// 조건 행 단위로 구워 두면 해석이 Build 에서 한 번만 일어난다.
		private static void buildPools()
		{
			Dictionary<int, Table_RewardItemPool.Row> all = Table_RewardItemPool.All();
			Dictionary<int, Table_RewardItemPool.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_RewardItemPool.Row row = e.Current.Value;
				if (row.PoolID <= 0)
				{
					continue;
				}

				PoolEntry entry = new PoolEntry();
				entry.row = row;
				collectCandidates(row, entry.candidates);

				List<PoolEntry> list;
				if (_byPool.TryGetValue(row.PoolID, out list) == false)
				{
					list = new List<PoolEntry>();
					_byPool.Add(row.PoolID, list);
				}

				list.Add(entry);
			}
		}

		// DropTier 는 **정확히 일치**다 — 이하 포함이 아니다 (설계 4.2).
		// 이하 포함으로 두면 후반 풀이 초반 아이템으로 희석된다.
		private static void collectCandidates(Table_RewardItemPool.Row cond, List<int> buffer)
		{
			Dictionary<int, Table_Item.Row> all = Table_Item.All();
			Dictionary<int, Table_Item.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Item.Row item = e.Current.Value;
				if (item.DropTier != cond.DropTier)
				{
					continue;
				}

				if (cond.MainCategory != ItemMainCategory.None && item.MainCategory != cond.MainCategory)
				{
					continue;
				}

				if (cond.SubCategory != ItemSubCategory.None && item.SubCategory != cond.SubCategory)
				{
					continue;
				}

				buffer.Add(item.ID);
			}
		}

		private static void buildRewards()
		{
			Dictionary<int, Table_Reward.Row> all = Table_Reward.All();
			Dictionary<int, Table_Reward.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Reward.Row row = e.Current.Value;
				if (row.GroupID <= 0)
				{
					continue;
				}

				RewardEntry entry = new RewardEntry();
				entry.row = row;
				entry.isValid = resolveTarget(row, entry);

				List<RewardEntry> list;
				if (_byGroup.TryGetValue(row.GroupID, out list) == false)
				{
					list = new List<RewardEntry>();
					_byGroup.Add(row.GroupID, list);
				}

				list.Add(entry);
			}
		}

		// TargetID 는 string 이라 RewardType 이 파싱 방법을 결정한다 (설계 3장).
		private static bool resolveTarget(Table_Reward.Row row, RewardEntry entry)
		{
			switch (row.RewardType)
			{
				case RewardType.Item:
				{
					int id;
					if (int.TryParse(row.TargetID, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) == false)
					{
						return false;
					}

					entry.itemId = id;
					return Table_Item.Get(id) != null;
				}

				case RewardType.ItemPool:
				{
					int id;
					if (int.TryParse(row.TargetID, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) == false)
					{
						return false;
					}

					entry.poolId = id;
					return _byPool.ContainsKey(id);
				}

				case RewardType.Currency:
				{
					EDT.Currency currency;
					if (Enum.TryParse<EDT.Currency>(row.TargetID, false, out currency) == false)
					{
						return false;
					}

					entry.currency = currency;
					return currency != EDT.Currency.None;
				}
			}

			return false;
		}
	}
}
