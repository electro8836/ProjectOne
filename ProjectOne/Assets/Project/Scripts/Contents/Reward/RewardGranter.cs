using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.Unit;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Reward
{
	// 보상 지급 맥락 (설계 3장).
	//
	// Reward 테이블은 "무엇을 얼마나" 만 갖고 **누가 왜 주는지는 모른다.** 그런데
	// Stat_GoldDropBonus 는 지급 맥락에 따라 적용 여부가 갈린다 — 같은 보상 그룹이
	// 몬스터 드랍과 퀘스트 보상 양쪽에 쓰일 수 있으므로 테이블만으로는 구분할 수 없다.
	//
	// 이 규칙은 데이터가 아니라 코드가 소유한다 (기반테이블 8.1).
	public enum RewardContext
	{
		None = 0,
		MonsterKill,
		QuestComplete,
		DungeonClear,
		ConsumableUse
	}

	// 실제로 지급된 것 하나. 결과창 표시와 서버 배치 업로드(STEP 14)가 같은 목록을 쓴다.
	public struct GrantedReward
	{
		public RewardType type;
		public int itemId;				// Item / ItemPool
		public EDT.Currency currency;	// Currency
		public int count;

		// 장비면 만들어진 인스턴스. 등급·순도·품질이 여기 들어 있다.
		public EquipmentInstance equipment;
	}

	// Reward 그룹을 굴려 실제로 지급한다 (설계 6장).
	//
	// **순수 로직에 가깝게 유지한다.** 입력은 테이블 + 맥락, 출력은 지급 목록이고
	// 부수효과는 인벤/지갑 반영뿐이다. 나중에 이 규칙을 서버(뒤끝 함수)로 포팅할 때
	// 기준이 되며, 권위를 넘길 때 호출부만 바꾸면 된다.
	public static class RewardGranter
	{
		// 가중치 추첨용 재사용 버퍼 — 지급은 메인 스레드 단일 경로다.
		private static readonly List<int> _weightBuffer = new List<int>(8);

		// 그룹 하나를 굴려 지급하고 결과를 buffer 에 채운다(호출자가 버퍼를 소유).
		// buffer 를 비우지 않고 **누적**한다 — 고유 드랍 + 지역 드랍처럼 두 그룹을 이어 굴릴 수 있다.
		public static void Grant(int groupId, RewardContext context, List<GrantedReward> buffer)
		{
			if (groupId <= 0 || buffer == null)
			{
				return;
			}

			IReadOnlyList<RewardCatalog.RewardEntry> entries = RewardCatalog.GetGroup(groupId);
			for (int i = 0; i < entries.Count; i++)
			{
				grantOne(entries[i], context, buffer);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void grantOne(RewardCatalog.RewardEntry entry, RewardContext context, List<GrantedReward> buffer)
		{
			if (entry.isValid == false)
			{
				return;		// 해석 실패 — 경고는 RewardCatalog.Build 가 이미 냈다
			}

			Table_Reward.Row row = entry.row;

			// [1] 확률 판정. 0 은 봉인이다 — 확정 지급은 1 을 적는다 (설계 3장).
			if (row.Chance <= 0f)
			{
				return;
			}

			if (row.Chance < 1f && Random.value >= row.Chance)
			{
				return;
			}

			// [2] 수량. MaxCount 가 0 이면 MinCount 고정.
			int count = rollCount(row);
			if (count <= 0)
			{
				return;
			}

			// [3] 타입 분기
			switch (row.RewardType)
			{
				case RewardType.Currency:
					grantCurrency(entry, context, count, buffer);
					break;

				case RewardType.Item:
					grantItem(entry.itemId, row.EquipGradeWeightID, count, buffer);
					break;

				case RewardType.ItemPool:
					// 각각 독립 추첨한다 — 2개면 서로 다른 아이템, 서로 다른 등급이 나올 수 있다 (설계 6.3).
					for (int n = 0; n < count; n++)
					{
						int itemId = pickFromPool(entry.poolId);
						if (itemId > 0)
						{
							grantItem(itemId, row.EquipGradeWeightID, 1, buffer);
						}
					}

					break;
			}
		}

		private static int rollCount(Table_Reward.Row row)
		{
			if (row.MaxCount <= 0 || row.MaxCount <= row.MinCount)
			{
				return row.MinCount;
			}

			return Random.Range(row.MinCount, row.MaxCount + 1);
		}

		// 골드 보너스는 **적 처치분에만** 곱한다 — 퀘스트·던전 클리어 보상은 고정값이다 (기반테이블 8.1).
		private static void grantCurrency(RewardCatalog.RewardEntry entry, RewardContext context, int count, List<GrantedReward> buffer)
		{
			if (context == RewardContext.MonsterKill)
			{
				count = Mathf.RoundToInt(count * (1f + getGoldDropBonus()));
			}

			if (count <= 0)
			{
				return;
			}

			CurrencyManager.Instance.Add(entry.currency, count);

			GrantedReward granted = default(GrantedReward);
			granted.type = RewardType.Currency;
			granted.currency = entry.currency;
			granted.count = count;
			buffer.Add(granted);
		}

		// 장비냐 아니냐는 RewardType 이 아니라 **최종 지급 대상**이 기준이다 (설계 6.1).
		private static void grantItem(int itemId, int gradeWeightId, int count, List<GrantedReward> buffer)
		{
			if (itemId <= 0 || count <= 0)
			{
				return;
			}

			if (Table_Equipment.Get(itemId) == null)
			{
				Account.Instance.Inventory.Add(itemId, count);

				GrantedReward stacked = default(GrantedReward);
				stacked.type = RewardType.Item;
				stacked.itemId = itemId;
				stacked.count = count;
				buffer.Add(stacked);
				return;
			}

			// 장비는 인스턴스 단위라 개수만큼 각각 굴린다.
			// EquipmentFactory 가 유효 등급 범위(Item.Grade ~ Equipment.MaxGrade)를 적용하고,
			// 가중치 합이 0이면 null 을 돌려준다 = 드랍 스킵 (설계 6.1).
			for (int i = 0; i < count; i++)
			{
				EquipmentInstance instance = EquipmentFactory.Create(itemId, gradeWeightId);
				if (instance == null)
				{
					continue;
				}

				Account.Instance.Inventory.AddEquipment(instance);

				GrantedReward granted = default(GrantedReward);
				granted.type = RewardType.Item;
				granted.itemId = itemId;
				granted.count = 1;
				granted.equipment = instance;
				buffer.Add(granted);
			}
		}

		// 풀 안에서 Weight 로 조건 행 1개 → 그 조건의 후보 중 균등 1개 (설계 6장).
		private static int pickFromPool(int poolId)
		{
			IReadOnlyList<RewardCatalog.PoolEntry> pool = RewardCatalog.GetPool(poolId);
			if (pool.Count == 0)
			{
				return 0;
			}

			_weightBuffer.Clear();
			for (int i = 0; i < pool.Count; i++)
			{
				// 후보가 없는 조건은 뽑히면 안 된다 — 뽑히면 그 회차가 통째로 날아간다.
				_weightBuffer.Add(pool[i].candidates.Count > 0 ? pool[i].row.Weight : 0);
			}

			int index = WeightedRandom.PickIndex(_weightBuffer);
			if (index < 0)
			{
				return 0;
			}

			List<int> candidates = pool[index].candidates;
			return candidates[Random.Range(0, candidates.Count)];
		}

		// 살아있는 히어로의 골드 획득량 보너스. 없으면 0.
		private static float getGoldDropBonus()
		{
			if (UnitContainer.HasInstance == false)
			{
				return 0f;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && hero.Stats != null)
				{
					return hero.Stats.GetStat(Stat.Stat_GoldDropBonus);
				}
			}

			return 0f;
		}
	}
}
