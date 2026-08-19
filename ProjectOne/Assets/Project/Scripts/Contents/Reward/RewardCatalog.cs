using System;
using System.Collections.Generic;
using System.Globalization;
using EDT;
using UnityEngine;

namespace ProjectOne.Reward
{
	// 보상 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// 설계의 핵심은 **보상을 "누가 주느냐"로 나누지 않는 것**이다.
	// 퀘스트·던전·몬스터·소모품이 Reward.GroupID 하나를 공유하고, 지급 API 가 맥락만 따로 받는다.
	//
	// RewardItemPool 은 열거식이 아니라 **조건형**이다 — 신규 아이템에 DropTier 만 적으면
	// 자동으로 드랍 풀에 편입되고 보상 테이블은 손댈 필요가 없다 (설계 4.1).
	// 그래서 조건 → 후보 목록을 Build 시점에 한 번 굽는다 (설계 6.2).
	//
	// MonsterCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class RewardCatalog
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

			// 해석에 실패한 행은 지급 대상에서 제외한다. 경고는 Build 가 이미 냈다.
			public bool isValid;
		}

		private static readonly Dictionary<int, List<RewardEntry>> _byGroup = new Dictionary<int, List<RewardEntry>>();
		private static readonly Dictionary<int, List<PoolEntry>> _byPool = new Dictionary<int, List<PoolEntry>>();

		private static readonly List<RewardEntry> _emptyRewards = new List<RewardEntry>();
		private static readonly List<PoolEntry> _emptyPools = new List<PoolEntry>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byGroup.Clear();
			_byPool.Clear();

			buildPools();
			buildRewards();

			_built = true;
			Debug.Log($"[RewardCatalog] 구축 완료 — 그룹:{_byGroup.Count} 풀:{_byPool.Count} 조건:{Table_RewardItemPool.All().Count} 보상행:{Table_Reward.All().Count}");

			validate();
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

		// ── 내부: 정합성 검증 (설계 8장) ──────────────────────────────

		// 여기서 나오는 경고 목록이 곧 채워야 할 엑셀 작업이다.
		// 컨버터가 이 검증을 넘겨받으면(STEP 15) 빌드 실패로 승격된다.
		private static void validate()
		{
			int issues = 0;
			issues += validatePools();
			issues += validateRewards();

			if (issues > 0)
			{
				Debug.LogWarning($"[RewardCatalog] 데이터 정합성 문제 {issues}건 — 위 경고 목록이 채워야 할 엑셀 작업입니다.");
			}
		}

		private static int validatePools()
		{
			int issues = 0;

			Dictionary<int, List<PoolEntry>>.Enumerator pe = _byPool.GetEnumerator();
			while (pe.MoveNext() == true)
			{
				int poolId = pe.Current.Key;
				List<PoolEntry> entries = pe.Current.Value;

				int weightSum = 0;
				for (int i = 0; i < entries.Count; i++)
				{
					PoolEntry entry = entries[i];
					weightSum += entry.row.Weight;

					// 비워두면 드랍 금지 아이템(DropTier=None)이 통째로 풀에 들어온다.
					if (entry.row.DropTier == DropTier.None)
					{
						Debug.LogWarning($"[RewardCatalog] 조건 {entry.row.ID}(풀 {poolId}) 의 DropTier 가 비었습니다 — 드랍 금지 아이템이 풀에 들어옵니다.");
						issues++;
					}

					// 에러 없이 조용히 아무것도 안 나오는 가장 흔한 사고다.
					if (entry.candidates.Count == 0)
					{
						Debug.LogWarning($"[RewardCatalog] 조건 {entry.row.ID}(풀 {poolId}, {entry.row.DropTier}/{entry.row.MainCategory}/{entry.row.SubCategory})에 매칭되는 아이템이 없습니다.");
						issues++;
					}
				}

				if (weightSum <= 0)
				{
					Debug.LogWarning($"[RewardCatalog] 풀 {poolId} 의 Weight 합이 0입니다 — 아무것도 뽑히지 않습니다.");
					issues++;
				}
			}

			return issues;
		}

		private static int validateRewards()
		{
			int issues = 0;
			int sealedCount = 0;

			Dictionary<int, List<RewardEntry>>.Enumerator ge = _byGroup.GetEnumerator();
			while (ge.MoveNext() == true)
			{
				List<RewardEntry> entries = ge.Current.Value;
				for (int i = 0; i < entries.Count; i++)
				{
					RewardEntry entry = entries[i];
					Table_Reward.Row row = entry.row;

					if (entry.isValid == false)
					{
						Debug.LogWarning($"[RewardCatalog] Reward {row.ID} 의 TargetID '{row.TargetID}' 를 {row.RewardType} 으로 해석하지 못했습니다.");
						issues++;
					}

					// 0 = 봉인. 확정 지급은 1 을 적어야 한다 (설계 3장 — 빈칸과 봉인을 구분하기 위해 필수 컬럼이다).
					if (row.Chance <= 0f)
					{
						sealedCount++;
					}

					if (row.MaxCount != 0 && row.MaxCount < row.MinCount)
					{
						Debug.LogWarning($"[RewardCatalog] Reward {row.ID} 의 MaxCount({row.MaxCount})가 MinCount({row.MinCount})보다 작습니다.");
						issues++;
					}

					issues += validateGradeWeight(entry);
				}
			}

			if (sealedCount > 0)
			{
				Debug.LogWarning($"[RewardCatalog] Chance 가 0 인 행 {sealedCount}건 — 봉인 상태라 지급되지 않습니다. 확정 지급은 1 을 적어야 합니다.");
				issues += sealedCount;
			}

			return issues;
		}

		// 지급 대상에 장비가 포함될 수 있으면 EquipGradeWeightID 가 필수다 (설계 6.1).
		// "비었으면 Item.Grade 로 고정" 같은 자동 폴백을 두지 않는다 —
		// 의도적 고정인지 안 채운 실수인지 구분할 수 없어진다.
		private static int validateGradeWeight(RewardEntry entry)
		{
			Table_Reward.Row row = entry.row;

			if (row.RewardType == RewardType.Currency)
			{
				if (row.EquipGradeWeightID != 0)
				{
					Debug.LogWarning($"[RewardCatalog] Reward {row.ID} 는 재화인데 EquipGradeWeightID 가 지정돼 있습니다.");
					return 1;
				}

				return 0;
			}

			bool mayBeEquipment = canYieldEquipment(entry);

			if (row.EquipGradeWeightID == 0)
			{
				if (mayBeEquipment == true)
				{
					Debug.LogWarning($"[RewardCatalog] Reward {row.ID} 는 장비가 나올 수 있는데 EquipGradeWeightID 가 비었습니다.");
					return 1;
				}

				return 0;
			}

			if (Table_EquipGradeWeight.Get(row.EquipGradeWeightID) == null)
			{
				Debug.LogWarning($"[RewardCatalog] Reward {row.ID} 의 EquipGradeWeightID {row.EquipGradeWeightID} 가 EquipGradeWeight 에 없습니다.");
				return 1;
			}

			return 0;
		}

		private static bool canYieldEquipment(RewardEntry entry)
		{
			if (entry.isValid == false)
			{
				return false;	// 해석 실패는 별도 경고로 이미 잡혔다
			}

			if (entry.row.RewardType == RewardType.Item)
			{
				return Table_Equipment.Get(entry.itemId) != null;
			}

			if (entry.row.RewardType != RewardType.ItemPool)
			{
				return false;
			}

			IReadOnlyList<PoolEntry> pool = GetPool(entry.poolId);
			for (int i = 0; i < pool.Count; i++)
			{
				List<int> candidates = pool[i].candidates;
				for (int n = 0; n < candidates.Count; n++)
				{
					if (Table_Equipment.Get(candidates[n]) != null)
					{
						return true;
					}
				}
			}

			return false;
		}
	}
}
