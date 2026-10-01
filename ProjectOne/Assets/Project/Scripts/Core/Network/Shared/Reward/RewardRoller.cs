using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 추첨 중 데이터 문제 보고 — 클라는 Debug 로그로 연결하고 서버는 null 을 넘긴다.
	public delegate void RewardRollLog(bool isError, string message);

	// Reward 그룹을 굴려 결과 목록을 만든다 (보상 설계 6장). 클라·서버 공용.
	//
	// **굴리기만 한다.** 인벤/지갑 반영은 클라(RewardGranter)와 서버(RewardApplier)가 각자 한다.
	// 같은 테이블·같은 규칙을 양쪽이 이 코드 한 벌로 돌리므로 확률이 어긋날 수 없다.
	public static class RewardRoller
	{
		// 가중치 추첨용 재사용 버퍼.
		private static readonly List<int> _weightBuffer = new List<int>(8);

		// Chance(0~1) 를 정수로 비교하기 위한 분모 — 백만분율(ppm).
		private const int ChanceScale = 1000000;

		// 그룹 하나를 굴려 buffer 에 **누적**한다(비우지 않는다).
		// currencyBonusPermille — 재화 수량 보너스(퍼밀). 골드 보너스처럼 맥락에 따라 갈리는 값을 호출자가 정한다.
		public static void Roll(int groupId, int currencyBonusPermille, IRandomSource rng, List<RolledReward> buffer, RewardRollLog log)
		{
			if (groupId <= 0 || buffer == null)
			{
				return;
			}

			IReadOnlyList<RewardTable.RewardEntry> entries = RewardTable.GetGroup(groupId);
			for (int i = 0; i < entries.Count; i++)
			{
				rollOne(entries[i], currencyBonusPermille, rng, buffer, log);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void rollOne(RewardTable.RewardEntry entry, int currencyBonusPermille, IRandomSource rng, List<RolledReward> buffer, RewardRollLog log)
		{
			if (entry.isValid == false)
			{
				return;		// 해석 실패 — 경고는 클라 RewardCatalog 검증이 이미 냈다
			}

			Table_Reward.Row row = entry.row;

			// [1] 확률 판정. 0 은 봉인이다 — 확정 지급은 1 을 적는다 (설계 3장).
			if (row.Chance <= 0f)
			{
				return;
			}

			// 실수 비교 대신 ppm 정수 비교 — 플랫폼별 실수 오차가 시드 재현을 깨지 않게 한다.
			if (row.Chance < 1f && rng.Range(0, ChanceScale) >= toPpm(row.Chance))
			{
				return;
			}

			// [2] 수량. MaxCount 가 0 이면 MinCount 고정.
			int count = rollCount(row, rng);
			if (count <= 0)
			{
				return;
			}

			// [3] 타입 분기
			switch (row.RewardType)
			{
				case RewardType.Currency:
					rollCurrency(entry, currencyBonusPermille, count, buffer);
					break;

				case RewardType.Item:
					rollItem(entry.itemId, row, count, rng, buffer, log);
					break;

				case RewardType.ItemPool:
					// 각각 독립 추첨한다 — 2개면 서로 다른 아이템, 서로 다른 등급이 나올 수 있다 (설계 6.3).
					for (int n = 0; n < count; n++)
					{
						int itemId = pickFromPool(entry.poolId, rng);
						if (itemId > 0)
						{
							rollItem(itemId, row, 1, rng, buffer, log);
						}
					}

					break;
			}
		}

		private static int rollCount(Table_Reward.Row row, IRandomSource rng)
		{
			if (row.MaxCount <= 0 || row.MaxCount <= row.MinCount)
			{
				return row.MinCount;
			}

			return rng.Range(row.MinCount, row.MaxCount + 1);
		}

		// float → double 변환과 double 곱셈은 IEEE 로 결정적이다.
		private static int toPpm(float chance)
		{
			return (int)System.Math.Round((double)chance * ChanceScale);
		}

		private static void rollCurrency(RewardTable.RewardEntry entry, int currencyBonusPermille, int count, List<RolledReward> buffer)
		{
			if (currencyBonusPermille != 0)
			{
				count = Permille.Apply(count, currencyBonusPermille);
			}

			if (count <= 0)
			{
				return;
			}

			RolledReward rolled = default(RolledReward);
			rolled.type = RewardType.Currency;
			rolled.currency = entry.currency;
			rolled.count = count;
			buffer.Add(rolled);
		}

		// 장비냐 아니냐는 RewardType 이 아니라 **최종 지급 대상**이 기준이다 (설계 6.1).
		private static void rollItem(int itemId, Table_Reward.Row row, int count, IRandomSource rng, List<RolledReward> buffer, RewardRollLog log)
		{
			if (itemId <= 0 || count <= 0)
			{
				return;
			}

			if (Table_Equipment.Get(itemId) == null)
			{
				RolledReward stacked = default(RolledReward);
				stacked.type = RewardType.Item;
				stacked.itemId = itemId;
				stacked.count = count;
				buffer.Add(stacked);
				return;
			}

			// 장비는 인스턴스 단위라 개수만큼 각각 굴린다.
			for (int i = 0; i < count; i++)
			{
				RolledReward rolled = default(RolledReward);
				rolled.type = RewardType.Item;
				rolled.itemId = itemId;
				rolled.count = 1;
				rolled.isEquipment = true;

				// FixedGrade 가 적혀 있으면 확정 상품이다 — 미리보기와 같은 값을 그대로 지급한다.
				if (row.FixedGrade != ItemGradeType.None)
				{
					rolled.grade = row.FixedGrade;
					rolled.quality = row.FixedQuality;
					buffer.Add(rolled);
					continue;
				}

				// 유효 등급 범위(Item.Grade ~ Equipment.MaxGrade)의 가중치 합이 0이면 드랍 스킵 (설계 6.1).
				EquipmentGradeRollResult result = EquipmentRoll.TryRollGrade(itemId, row.EquipGradeWeightID, rng, out rolled.grade);
				if (result != EquipmentGradeRollResult.Success)
				{
					reportGradeFailure(itemId, row.EquipGradeWeightID, result, log);
					continue;
				}

				if (EquipmentRoll.TryRollQuality(rng, out rolled.quality) == false && log != null)
				{
					log(false, "[EquipmentFactory] EquipQuality 가중치가 없어 품질 1 로 대체합니다.");
				}

				buffer.Add(rolled);
			}
		}

		private static void reportGradeFailure(int itemId, int gradeWeightId, EquipmentGradeRollResult result, RewardRollLog log)
		{
			if (log == null)
			{
				return;
			}

			switch (result)
			{
				case EquipmentGradeRollResult.NotEquipment:
					log(true, $"[EquipmentFactory] 장비 아이템이 아닙니다: {itemId}");
					break;
				case EquipmentGradeRollResult.MissingWeight:
					log(true, $"[EquipmentFactory] EquipGradeWeight {gradeWeightId} 를 찾지 못했습니다.");
					log(false, $"[EquipmentFactory] 유효 등급이 없어 드랍을 스킵합니다 — item:{itemId} weightGroup:{gradeWeightId}");
					break;
				case EquipmentGradeRollResult.NoValidGrade:
					log(false, $"[EquipmentFactory] 유효 등급이 없어 드랍을 스킵합니다 — item:{itemId} weightGroup:{gradeWeightId}");
					break;
			}
		}

		// 풀 안에서 Weight 로 조건 행 1개 → 그 조건의 후보 중 균등 1개 (설계 6장).
		private static int pickFromPool(int poolId, IRandomSource rng)
		{
			IReadOnlyList<RewardTable.PoolEntry> pool = RewardTable.GetPool(poolId);
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

			int index = WeightedPick.PickIndex(_weightBuffer, rng);
			if (index < 0)
			{
				return 0;
			}

			List<int> candidates = pool[index].candidates;
			return candidates[rng.Range(0, candidates.Count)];
		}
	}
}
