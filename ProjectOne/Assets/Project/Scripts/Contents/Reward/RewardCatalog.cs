using System.Collections.Generic;
using EDT;
using ProjectOne.Shared;
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
		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		// 인덱싱은 공유 코어(RewardTable)가 하고, 여기서는 그 위에 정합성 검증 로그만 얹는다.
		public static void Build()
		{
			RewardTable.Build();

			_built = true;
			Debug.Log($"[RewardCatalog] 구축 완료 — 그룹:{RewardTable.GroupCount} 풀:{RewardTable.PoolCount} 조건:{Table_RewardItemPool.All().Count} 보상행:{Table_Reward.All().Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 보상 그룹 하나. 소비처가 이 목록을 통째로 굴린다.
		public static IReadOnlyList<RewardTable.RewardEntry> GetGroup(int groupId)
		{
			return RewardTable.GetGroup(groupId);
		}

		public static IReadOnlyList<RewardTable.PoolEntry> GetPool(int poolId)
		{
			return RewardTable.GetPool(poolId);
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

			Dictionary<int, List<RewardTable.PoolEntry>>.Enumerator pe = RewardTable.GetPoolEnumerator();
			while (pe.MoveNext() == true)
			{
				int poolId = pe.Current.Key;
				List<RewardTable.PoolEntry> entries = pe.Current.Value;

				int weightSum = 0;
				for (int i = 0; i < entries.Count; i++)
				{
					RewardTable.PoolEntry entry = entries[i];
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

			Dictionary<int, List<RewardTable.RewardEntry>>.Enumerator ge = RewardTable.GetGroupEnumerator();
			while (ge.MoveNext() == true)
			{
				List<RewardTable.RewardEntry> entries = ge.Current.Value;
				for (int i = 0; i < entries.Count; i++)
				{
					RewardTable.RewardEntry entry = entries[i];
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
		private static int validateGradeWeight(RewardTable.RewardEntry entry)
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

			// FixedGrade 로 등급을 확정한 행은 가중치를 쓰지 않는다 — 확정 지급 상품(상점 패키지 등)이 그렇다.
			if (row.FixedGrade != ItemGradeType.None)
			{
				if (row.EquipGradeWeightID != 0)
				{
					Debug.LogWarning($"[RewardCatalog] Reward {row.ID} 는 FixedGrade 가 있어 EquipGradeWeightID 가 무시됩니다.");
					return 1;
				}

				return 0;
			}

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

		private static bool canYieldEquipment(RewardTable.RewardEntry entry)
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

			IReadOnlyList<RewardTable.PoolEntry> pool = GetPool(entry.poolId);
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
