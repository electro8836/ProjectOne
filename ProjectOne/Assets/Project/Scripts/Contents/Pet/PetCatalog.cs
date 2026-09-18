using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Pets
{
	// 펫 테이블 3종의 조회를 모아 둔 런타임 인덱스.
	//
	// Table_PetEnhance / Table_PetPromotion 은 Get(int id) 밖에 없어서 등급·레벨로 찾으려면
	// 매번 All() 을 훑어야 한다 — 최초 1회 접어 두고 쓴다 (MasteryCatalog 와 같은 이유).
	public static class PetCatalog
	{
		// 강화 비용 구간. PetEnhance 행을 MaxLevel 오름차순으로 세운 것이다.
		private static readonly List<Table_PetEnhance.Row> _enhanceTiers = new List<Table_PetEnhance.Row>();

		// 등급 -> 그 등급이 허용하는 최대 레벨
		private static readonly Dictionary<ItemGradeType, int> _maxLevels = new Dictionary<ItemGradeType, int>();

		// 현재 등급 -> 승급 행
		private static readonly Dictionary<ItemGradeType, Table_PetPromotion.Row> _promotions = new Dictionary<ItemGradeType, Table_PetPromotion.Row>();

		// ID 오름차순 목록 — Dictionary 는 순서를 보장하지 않으므로 표시 순서는 여기서 고정한다.
		private static readonly List<Table_Pet.Row> _sorted = new List<Table_Pet.Row>();

		private static bool _isBuilt;

		public static bool IsBuilt
		{
			get { return _isBuilt; }
		}

		// 표시 대상 펫 전체 수 — 펫 창 타이틀의 분모.
		public static int Count
		{
			get { return _sorted.Count; }
		}

		public static IReadOnlyList<Table_Pet.Row> AllSorted()
		{
			return _sorted;
		}

		// 테이블 로드 이후 부트에서 1회 호출한다.
		public static void Build()
		{
			_sorted.Clear();
			_enhanceTiers.Clear();
			_maxLevels.Clear();
			_promotions.Clear();

			Dictionary<Pet, Table_Pet.Row> pets = Table_Pet.All();
			Dictionary<Pet, Table_Pet.Row>.Enumerator pe = pets.GetEnumerator();
			while (pe.MoveNext() == true)
			{
				Table_Pet.Row row = pe.Current.Value;
				if (row.ID == Pet.None)
				{
					continue;
				}

				_sorted.Add(row);
			}

			_sorted.Sort(comparePetId);

			Dictionary<int, Table_PetEnhance.Row> enhances = Table_PetEnhance.All();
			Dictionary<int, Table_PetEnhance.Row>.Enumerator ee = enhances.GetEnumerator();
			while (ee.MoveNext() == true)
			{
				Table_PetEnhance.Row row = ee.Current.Value;
				_enhanceTiers.Add(row);

				if (row.Grade != ItemGradeType.None)
				{
					_maxLevels[row.Grade] = row.MaxLevel;
				}
			}

			_enhanceTiers.Sort(compareMaxLevel);

			Dictionary<int, Table_PetPromotion.Row> promotions = Table_PetPromotion.All();
			Dictionary<int, Table_PetPromotion.Row>.Enumerator pre = promotions.GetEnumerator();
			while (pre.MoveNext() == true)
			{
				Table_PetPromotion.Row row = pre.Current.Value;
				if (row.FromGrade == ItemGradeType.None)
				{
					continue;
				}

				_promotions[row.FromGrade] = row;
			}

			_isBuilt = true;
		}

		public static Table_Pet.Row Get(Pet id)
		{
			return Table_Pet.Get(id);
		}

		// 등급이 허용하는 최대 강화 레벨. 행이 없으면 0 — 호출부가 강화를 막는다.
		public static int GetMaxLevel(ItemGradeType grade)
		{
			int maxLevel;
			if (_maxLevels.TryGetValue(grade, out maxLevel) == false)
			{
				return 0;
			}

			return maxLevel;
		}

		// 다음 등급으로 가는 승급 행. 최고 등급이면 null.
		public static Table_PetPromotion.Row GetPromotion(ItemGradeType from)
		{
			Table_PetPromotion.Row row;
			_promotions.TryGetValue(from, out row);
			return row;
		}

		// level 레벨에서 다음 레벨로 올리는 비용.
		//
		// 비용 구간은 **등급이 아니라 현재 레벨**이 정한다 — 등급을 올려도 레벨이 낮으면 싼 구간을 그대로 쓴다.
		// PetEnhance 행을 누적 구간표(1~20 / 21~40 / ...)로 읽고, 증가량은 구간 안 상대 레벨에 곱한다.
		// 그래서 구간이 바뀌는 순간 증가분이 0으로 돌아가고 기준값이 새 티어의 가격을 결정한다.
		//
		// 증가식은 EquipmentUpgrade.scale 과 같다 (그쪽이 private static 이라 공유하지 못한다).
		public static bool TryGetEnhanceCost(int level, out EDT.Currency currency, out int amount)
		{
			currency = EDT.Currency.None;
			amount = 0;

			if (level < 1)
			{
				return false;
			}

			int tierFloor = 0;	// 직전 구간의 MaxLevel — 구간 안 상대 레벨의 기준점
			for (int i = 0; i < _enhanceTiers.Count; i++)
			{
				Table_PetEnhance.Row tier = _enhanceTiers[i];
				if (level > tier.MaxLevel)
				{
					tierFloor = tier.MaxLevel;
					continue;
				}

				int step = level - tierFloor - 1;
				currency = tier.CostCurrencyID;
				amount = Mathf.RoundToInt(tier.CostCurrencyValue * (1f + tier.CostCurrencyMult * step));
				return currency != EDT.Currency.None && amount > 0;
			}

			// 마지막 구간의 MaxLevel 을 넘었다 — 더 올릴 곳이 없다.
			return false;
		}

		private static int comparePetId(Table_Pet.Row a, Table_Pet.Row b)
		{
			return ((int)a.ID).CompareTo((int)b.ID);
		}

		private static int compareMaxLevel(Table_PetEnhance.Row a, Table_PetEnhance.Row b)
		{
			return a.MaxLevel.CompareTo(b.MaxLevel);
		}
	}
}
