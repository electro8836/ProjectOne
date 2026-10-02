using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 출석 규칙 중 테이블만 보고 판단하는 부분. 클라·서버 공용.
	//
	// 종류(주간/월간)마다 IsDisabled 를 뺀 행을 DayCount 오름차순으로 세운 것이 한 사이클이다.
	// 사이클 길이가 순환 기준이다 — 마지막 일차를 받으면 1일차로 돌아간다.
	public static class DailyBonusRules
	{
		private static readonly List<Table_DailyBonus.Row> _empty = new List<Table_DailyBonus.Row>();

		// 종류 → DayCount 오름차순 행. 처음 쓸 때 만든다.
		private static Dictionary<DailyBonusType, List<Table_DailyBonus.Row>> _byType;

		// 종류 하나의 전체 일차. DayCount 오름차순이며 인덱스 0 이 1일차다.
		public static IReadOnlyList<Table_DailyBonus.Row> GetDays(DailyBonusType type)
		{
			ensureBuilt();

			List<Table_DailyBonus.Row> list;
			if (_byType.TryGetValue(type, out list) == true)
			{
				return list;
			}

			return _empty;
		}

		// dayCount 는 1부터다. 없으면 null.
		public static Table_DailyBonus.Row GetDay(DailyBonusType type, int dayCount)
		{
			IReadOnlyList<Table_DailyBonus.Row> list = GetDays(type);
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].DayCount == dayCount)
				{
					return list[i];
				}
			}

			return null;
		}

		// 한 사이클의 길이 — 순환 판정에 쓴다.
		public static int GetCycleLength(DailyBonusType type)
		{
			return GetDays(type).Count;
		}

		// 수령 후 다음에 받을 일차 — 주기 끝에서 1로 돌아온다.
		public static int NextDayCount(int dayCount, int cycleLength)
		{
			if (cycleLength <= 0)
			{
				return dayCount;
			}

			return (dayCount % cycleLength) + 1;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void ensureBuilt()
		{
			if (_byType != null)
			{
				return;
			}

			Dictionary<DailyBonusType, List<Table_DailyBonus.Row>> byType = new Dictionary<DailyBonusType, List<Table_DailyBonus.Row>>();

			Dictionary<int, Table_DailyBonus.Row>.Enumerator e = Table_DailyBonus.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_DailyBonus.Row row = e.Current.Value;
				if (row.IsDisabled == true || row.DailyBonusType == DailyBonusType.None)
				{
					continue;
				}

				List<Table_DailyBonus.Row> list;
				if (byType.TryGetValue(row.DailyBonusType, out list) == false)
				{
					list = new List<Table_DailyBonus.Row>();
					byType.Add(row.DailyBonusType, list);
				}

				list.Add(row);
			}

			Dictionary<DailyBonusType, List<Table_DailyBonus.Row>>.Enumerator se = byType.GetEnumerator();
			while (se.MoveNext() == true)
			{
				se.Current.Value.Sort(compareByDayCount);
			}

			_byType = byType;
		}

		private static int compareByDayCount(Table_DailyBonus.Row a, Table_DailyBonus.Row b)
		{
			return a.DayCount.CompareTo(b.DayCount);
		}
	}
}
