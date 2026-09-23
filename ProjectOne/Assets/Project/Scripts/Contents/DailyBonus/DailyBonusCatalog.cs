using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.DailyBonuses
{
	// 출석 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// 테이블은 ID 하나로 평평하게 깔려 있어 "주간 3일차" 를 찾으려면 매번 전체를 훑어야 한다.
	// 종류별 DayCount 오름차순 목록으로 한 번만 굽는다.
	//
	// RewardCatalog / QuestCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class DailyBonusCatalog
	{
		private static readonly Dictionary<DailyBonusType, List<Table_DailyBonus.Row>> _byType = new Dictionary<DailyBonusType, List<Table_DailyBonus.Row>>();

		private static readonly List<Table_DailyBonus.Row> _empty = new List<Table_DailyBonus.Row>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byType.Clear();

			Dictionary<int, Table_DailyBonus.Row> all = Table_DailyBonus.All();
			Dictionary<int, Table_DailyBonus.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_DailyBonus.Row row = e.Current.Value;
				if (row.IsDisabled == true)
				{
					continue;
				}

				if (row.DailyBonusType == DailyBonusType.None)
				{
					Debug.LogWarning($"[DailyBonusCatalog] ID {row.ID} 의 DailyBonusType 이 비어 있다 — 건너뛴다.");
					continue;
				}

				List<Table_DailyBonus.Row> list;
				if (_byType.TryGetValue(row.DailyBonusType, out list) == false)
				{
					list = new List<Table_DailyBonus.Row>();
					_byType.Add(row.DailyBonusType, list);
				}

				list.Add(row);
			}

			sortAll();

			_built = true;
			Debug.Log($"[DailyBonusCatalog] 구축 완료 — 종류:{_byType.Count} 행:{all.Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 종류 하나의 전체 일차. DayCount 오름차순이며 인덱스 0 이 1일차다.
		public static IReadOnlyList<Table_DailyBonus.Row> GetDays(DailyBonusType type)
		{
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

		// ── 내부 ──────────────────────────────────────────────────────

		private static void sortAll()
		{
			Dictionary<DailyBonusType, List<Table_DailyBonus.Row>>.Enumerator e = _byType.GetEnumerator();
			while (e.MoveNext() == true)
			{
				e.Current.Value.Sort(compareByDayCount);
			}
		}

		private static int compareByDayCount(Table_DailyBonus.Row a, Table_DailyBonus.Row b)
		{
			return a.DayCount.CompareTo(b.DayCount);
		}

		// 일차가 1부터 빈틈없이 이어지는지, 보상 그룹이 비어 있지 않은지 본다.
		// 중간이 비면 순환 카운터가 그 칸에서 영원히 멈춘다 — 조용히 깨지므로 여기서 잡는다.
		//
		// 보상 누락은 종류마다 한 줄로 묶는다 — 통째로 비어 있는 종류(아직 안 채운 신규 출석 등)가
		// 콘솔을 30줄씩 덮으면 진짜 구멍 하나를 못 본다.
		private static void validate()
		{
			System.Text.StringBuilder missing = new System.Text.StringBuilder();

			Dictionary<DailyBonusType, List<Table_DailyBonus.Row>>.Enumerator e = _byType.GetEnumerator();
			while (e.MoveNext() == true)
			{
				List<Table_DailyBonus.Row> list = e.Current.Value;

				missing.Clear();
				for (int i = 0; i < list.Count; i++)
				{
					Table_DailyBonus.Row row = list[i];
					if (row.DayCount != i + 1)
					{
						Debug.LogWarning($"[DailyBonusCatalog] {e.Current.Key} 의 일차가 이어지지 않는다 — {i + 1} 이어야 할 자리에 {row.DayCount} 가 있다.");
					}

					if (row.RewardGroupID <= 0)
					{
						if (missing.Length > 0)
						{
							missing.Append(", ");
						}

						missing.Append(row.DayCount);
					}
				}

				if (missing.Length > 0)
				{
					Debug.LogWarning($"[DailyBonusCatalog] {e.Current.Key} 의 RewardGroupID 가 비어 있다 — {missing}일차. DailyBonus 엑셀을 채워야 한다.");
				}
			}
		}
	}
}
