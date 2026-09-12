using System.Collections.Generic;
using EDT;
using ProjectOne.Items;

namespace ProjectOne.Reward
{
	// 보상 하나가 특정 등급으로 나올 확률.
	public struct RewardChance
	{
		public int itemId;
		public ItemGradeType grade;
		public float chance;			// 0~1, 1회 추첨 기준 절대확률
		public EquipmentInstance equipment;	// 장비면 그 등급이 반영된 표시용 인스턴스, 아니면 null
	}

	// 보상 그룹이 낼 수 있는 (아이템 × 등급) 조합과 그 확률을 모두 편다.
	//
	// RewardGranter 는 실제로 굴리고 여기는 굴리지 않는다 — 상자 확률표처럼 "무엇이 얼마나 나오는지"를 보여줄 때 쓴다.
	// 따라서 아래 계산은 RewardGranter.pickFromPool 과 EquipmentFactory.tryRollGrade 의 규칙을 그대로 따라야 한다.
	//
	//   P(아이템 i, 등급 g) = Chance × (조건W / 유효조건W합) × (1 / 조건내후보수) × (등급w / 범위내w합)
	//
	// 조건 내 아이템 선택은 균등이고, 후보가 없는 조건은 가중치 0 으로 빠진다.
	public static class RewardProbability
	{
		private static readonly List<RewardChance> _work = new List<RewardChance>();

		// buffer 는 비우지 않고 누적한다 (RewardGranter 와 같은 관례).
		public static void Build(int groupId, List<RewardChance> buffer)
		{
			if (groupId <= 0 || buffer == null)
			{
				return;
			}

			_work.Clear();

			IReadOnlyList<RewardCatalog.RewardEntry> entries = RewardCatalog.GetGroup(groupId);
			for (int i = 0; i < entries.Count; i++)
			{
				RewardCatalog.RewardEntry entry = entries[i];
				if (entry.isValid == false)
				{
					continue;
				}

				Table_Reward.Row row = entry.row;
				float chance = row.Chance;
				if (chance <= 0f)
				{
					// Chance 0 은 봉인이다 — RewardGranter 가 굴리지도 않는다.
					continue;
				}

				if (chance > 1f)
				{
					chance = 1f;
				}

				switch (row.RewardType)
				{
					case RewardType.Item:
						addItem(row, entry.itemId, chance);
						break;

					case RewardType.ItemPool:
						addPool(row, entry.poolId, chance);
						break;

					// 재화는 등급이 없어 등급별 목록에 낄 자리가 없다 — 확률표에서는 뺀다.
				}
			}

			sortAndAppend(buffer);
		}

		// ── 내부: 항목 펼치기 ─────────────────────────────────────────────

		private static void addPool(Table_Reward.Row row, int poolId, float chance)
		{
			IReadOnlyList<RewardCatalog.PoolEntry> pool = RewardCatalog.GetPool(poolId);

			// 후보가 없는 조건은 추첨에서 빠지므로 가중치 합에서도 빼야 한다.
			int weightSum = 0;
			for (int i = 0; i < pool.Count; i++)
			{
				if (pool[i].candidates.Count > 0 && pool[i].row.Weight > 0)
				{
					weightSum += pool[i].row.Weight;
				}
			}

			if (weightSum <= 0)
			{
				return;
			}

			for (int i = 0; i < pool.Count; i++)
			{
				RewardCatalog.PoolEntry cond = pool[i];
				if (cond.candidates.Count <= 0 || cond.row.Weight <= 0)
				{
					continue;
				}

				// 조건이 뽑힐 확률 × 그 안에서 아이템 하나가 뽑힐 확률(균등).
				float perItem = chance * ((float)cond.row.Weight / weightSum) / cond.candidates.Count;
				for (int k = 0; k < cond.candidates.Count; k++)
				{
					addItem(row, cond.candidates[k], perItem);
				}
			}
		}

		// 아이템 하나를 등급별로 쪼개 넣는다.
		private static void addItem(Table_Reward.Row row, int itemId, float chance)
		{
			if (itemId <= 0 || chance <= 0f)
			{
				return;
			}

			Table_Item.Row item = Table_Item.Get(itemId);
			if (item == null)
			{
				return;
			}

			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);
			if (equipment == null)
			{
				// 비장비는 등급 추첨이 없다 — Item.Grade 고정이다.
				accumulate(itemId, item.Grade, chance, null);
				return;
			}

			// 확정 등급이 적힌 행은 그 등급 하나로만 나온다.
			if (row.FixedGrade != ItemGradeType.None)
			{
				accumulate(itemId, row.FixedGrade, chance, makeInstance(itemId, row.FixedGrade));
				return;
			}

			addEquipmentGrades(row, item, equipment, itemId, chance);
		}

		// EquipmentFactory.tryRollGrade 와 같은 규칙으로 등급 분포를 만든다.
		private static void addEquipmentGrades(Table_Reward.Row row, Table_Item.Row item, Table_Equipment.Row equipment, int itemId, float chance)
		{
			Table_EquipGradeWeight.Row weights = Table_EquipGradeWeight.Get(row.EquipGradeWeightID);
			if (weights == null)
			{
				// 가중치 행이 없으면 드랍 자체가 스킵된다 — 나올 수 없으니 목록에서도 뺀다.
				return;
			}

			int min = (int)item.Grade;
			int max = (int)equipment.MaxGrade;
			if (min <= 0)
			{
				min = (int)ItemGradeType.Normal;
			}

			if (max <= 0)
			{
				max = (int)ItemGradeType.Mythic;
			}

			int total = 0;
			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				if (g >= min && g <= max)
				{
					total += getWeight(weights, (ItemGradeType)g);
				}
			}

			if (total <= 0)
			{
				return;
			}

			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				if (g < min || g > max)
				{
					continue;
				}

				int w = getWeight(weights, (ItemGradeType)g);
				if (w <= 0)
				{
					continue;
				}

				ItemGradeType grade = (ItemGradeType)g;
				accumulate(itemId, grade, chance * ((float)w / total), makeInstance(itemId, grade));
			}
		}

		private static EquipmentInstance makeInstance(int itemId, ItemGradeType grade)
		{
			return EquipmentFactory.CreateExact(itemId, grade, RewardPreview.DEFAULT_PURITY, RewardPreview.DEFAULT_QUALITY);
		}

		private static int getWeight(Table_EquipGradeWeight.Row row, ItemGradeType grade)
		{
			switch (grade)
			{
				case ItemGradeType.Normal:	return row.Normal;
				case ItemGradeType.Magic:	return row.Magic;
				case ItemGradeType.Rare:	return row.Rare;
				case ItemGradeType.Epic:	return row.Epic;
				case ItemGradeType.Legendary:	return row.Legendary;
				case ItemGradeType.Mythic:	return row.Mythic;
			}

			return 0;
		}

		// 같은 (아이템, 등급) 이 여러 조건에서 나올 수 있다 — 합산한다.
		private static void accumulate(int itemId, ItemGradeType grade, float chance, EquipmentInstance instance)
		{
			for (int i = 0; i < _work.Count; i++)
			{
				if (_work[i].itemId != itemId || _work[i].grade != grade)
				{
					continue;
				}

				RewardChance merged = _work[i];
				merged.chance += chance;
				_work[i] = merged;
				return;
			}

			RewardChance added = default(RewardChance);
			added.itemId = itemId;
			added.grade = grade;
			added.chance = chance;
			added.equipment = instance;
			_work.Add(added);
		}

		// 등급 오름차순 → 확률 내림차순. 목록이 수십 건이라 삽입정렬로 충분하다.
		private static void sortAndAppend(List<RewardChance> buffer)
		{
			for (int i = 1; i < _work.Count; i++)
			{
				RewardChance key = _work[i];
				int j = i - 1;
				while (j >= 0 && isAfter(_work[j], key) == true)
				{
					_work[j + 1] = _work[j];
					j--;
				}

				_work[j + 1] = key;
			}

			for (int i = 0; i < _work.Count; i++)
			{
				buffer.Add(_work[i]);
			}
		}

		private static bool isAfter(RewardChance a, RewardChance b)
		{
			if (a.grade != b.grade)
			{
				return (int)a.grade > (int)b.grade;
			}

			return a.chance < b.chance;
		}
	}
}
