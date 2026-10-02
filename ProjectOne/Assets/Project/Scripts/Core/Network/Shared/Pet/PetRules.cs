using System;
using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 펫 강화·승급 규칙 중 테이블만 보고 판단하는 부분. 클라·서버 공용.
	//
	// 클라는 버튼 상태·비용 표시에 쓰고, 서버는 같은 규칙으로 요청을 검증한 뒤 차감·변경한다.
	// 보유 여부와 재화 부족은 호출자가 판정한다 — 클라는 로컬 상태로, 서버는 저장값으로.
	//
	// Table_PetEnhance / Table_PetPromotion 은 Get(int id) 밖에 없어 등급·레벨로 찾으려면
	// 매번 All() 을 훑어야 한다 — 처음 쓸 때 1회 접어 둔다.
	public static class PetRules
	{
		// 강화 비용 구간. PetEnhance 행을 MaxLevel 오름차순으로 세운 것이다.
		private static List<Table_PetEnhance.Row> _enhanceTiers;

		// 등급 -> 그 등급이 허용하는 최대 레벨
		private static Dictionary<ItemGradeType, int> _maxLevels;

		// 현재 등급 -> 승급 행
		private static Dictionary<ItemGradeType, Table_PetPromotion.Row> _promotions;

		// 등급이 허용하는 최대 강화 레벨. 행이 없으면 0 — 호출부가 강화를 막는다.
		public static int GetMaxLevel(ItemGradeType grade)
		{
			ensureBuilt();

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
			ensureBuilt();

			Table_PetPromotion.Row row;
			_promotions.TryGetValue(from, out row);
			return row;
		}

		// level 레벨에서 다음 레벨로 올리는 비용.
		//
		// 비용 구간은 **등급이 아니라 현재 레벨**이 정한다 — 등급을 올려도 레벨이 낮으면 싼 구간을 그대로 쓴다.
		// PetEnhance 행을 누적 구간표(1~20 / 21~40 / ...)로 읽고, 증가량은 구간 안 상대 레벨에 곱한다.
		// 그래서 구간이 바뀌는 순간 증가분이 0으로 돌아가고 기준값이 새 티어의 가격을 결정한다.
		// 반올림은 Mathf.RoundToInt 와 같은 Math.Round(은행원 반올림)다 — 클라·서버 값이 같아야 한다.
		public static bool TryGetEnhanceCost(int level, out EDT.Currency currency, out int amount)
		{
			ensureBuilt();

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
				amount = (int)Math.Round(tier.CostCurrencyValue * (1f + tier.CostCurrencyMult * step));
				return currency != EDT.Currency.None && amount > 0;
			}

			// 마지막 구간의 MaxLevel 을 넘었다 — 더 올릴 곳이 없다.
			return false;
		}

		// 보유한 펫의 강화 판정 — MaxLevel / NoTable 만 낸다(NotOwned·NotEnough 는 호출자).
		public static PetEnhanceBlock GetEnhanceBlock(int level, ItemGradeType grade)
		{
			int maxLevel = GetMaxLevel(grade);
			if (maxLevel > 0 && level >= maxLevel)
			{
				return PetEnhanceBlock.MaxLevel;
			}

			EDT.Currency currency;
			int amount;
			if (TryGetEnhanceCost(level, out currency, out amount) == false)
			{
				return PetEnhanceBlock.NoTable;
			}

			return PetEnhanceBlock.None;
		}

		// 보유한 펫의 승급 판정 — MaxGrade / NoTable 만 낸다(NotOwned·NotEnough 는 호출자).
		// 레벨 조건은 보지 않는다 — PetPromotion 테이블에 그런 컬럼이 없다.
		public static PetPromoteBlock GetPromoteBlock(ItemGradeType grade)
		{
			Table_PetPromotion.Row row = GetPromotion(grade);
			if (row == null)
			{
				return PetPromoteBlock.MaxGrade;
			}

			if (row.ToGrade == ItemGradeType.None || row.CostCurrencyID == EDT.Currency.None)
			{
				return PetPromoteBlock.NoTable;
			}

			return PetPromoteBlock.None;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 테이블 로드 후 처음 쓸 때 만든다. 승급 인덱스를 마지막에 넣어 완료 표시를 겸한다.
		private static void ensureBuilt()
		{
			if (_promotions != null)
			{
				return;
			}

			List<Table_PetEnhance.Row> tiers = new List<Table_PetEnhance.Row>();
			Dictionary<ItemGradeType, int> maxLevels = new Dictionary<ItemGradeType, int>();

			Dictionary<int, Table_PetEnhance.Row> enhances = Table_PetEnhance.All();
			Dictionary<int, Table_PetEnhance.Row>.Enumerator ee = enhances.GetEnumerator();
			while (ee.MoveNext() == true)
			{
				Table_PetEnhance.Row row = ee.Current.Value;
				tiers.Add(row);

				if (row.Grade != ItemGradeType.None)
				{
					maxLevels[row.Grade] = row.MaxLevel;
				}
			}

			tiers.Sort(compareMaxLevel);

			Dictionary<ItemGradeType, Table_PetPromotion.Row> promotions = new Dictionary<ItemGradeType, Table_PetPromotion.Row>();
			Dictionary<int, Table_PetPromotion.Row> rows = Table_PetPromotion.All();
			Dictionary<int, Table_PetPromotion.Row>.Enumerator pe = rows.GetEnumerator();
			while (pe.MoveNext() == true)
			{
				Table_PetPromotion.Row row = pe.Current.Value;
				if (row.FromGrade == ItemGradeType.None)
				{
					continue;
				}

				promotions[row.FromGrade] = row;
			}

			_enhanceTiers = tiers;
			_maxLevels = maxLevels;
			_promotions = promotions;
		}

		private static int compareMaxLevel(Table_PetEnhance.Row a, Table_PetEnhance.Row b)
		{
			return a.MaxLevel.CompareTo(b.MaxLevel);
		}
	}
}
