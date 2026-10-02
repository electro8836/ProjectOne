using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Shared;

namespace ProjectOne.DailyBonuses
{
	// 출석 조회 + 데이터 정합성 검증.
	//
	// 종류별 DayCount 오름차순 목록은 서버와 같이 쓰도록 공유 DailyBonusRules 가 소유한다. 여기서는 위임만 한다.
	//
	// RewardCatalog / QuestCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class DailyBonusCatalog
	{
		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			Dictionary<int, Table_DailyBonus.Row> all = Table_DailyBonus.All();
			Dictionary<int, Table_DailyBonus.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_DailyBonus.Row row = e.Current.Value;
				if (row.IsDisabled == false && row.DailyBonusType == DailyBonusType.None)
				{
					Debug.LogWarning($"[DailyBonusCatalog] ID {row.ID} 의 DailyBonusType 이 비어 있다 — 건너뛴다.");
				}
			}

			_built = true;
			Debug.Log($"[DailyBonusCatalog] 구축 완료 — 행:{all.Count}");

			System.Array types = System.Enum.GetValues(typeof(DailyBonusType));
			for (int i = 0; i < types.Length; i++)
			{
				DailyBonusType type = (DailyBonusType)types.GetValue(i);
				if (type != DailyBonusType.None)
				{
					validate(type);
				}
			}
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 종류 하나의 전체 일차. DayCount 오름차순이며 인덱스 0 이 1일차다.
		public static IReadOnlyList<Table_DailyBonus.Row> GetDays(DailyBonusType type)
		{
			return DailyBonusRules.GetDays(type);
		}

		// dayCount 는 1부터다. 없으면 null.
		public static Table_DailyBonus.Row GetDay(DailyBonusType type, int dayCount)
		{
			return DailyBonusRules.GetDay(type, dayCount);
		}

		// 한 사이클의 길이 — 순환 판정에 쓴다.
		public static int GetCycleLength(DailyBonusType type)
		{
			return DailyBonusRules.GetCycleLength(type);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 일차가 1부터 빈틈없이 이어지는지, 보상 그룹이 비어 있지 않은지 본다.
		// 중간이 비면 순환 카운터가 그 칸에서 영원히 멈춘다 — 조용히 깨지므로 여기서 잡는다.
		//
		// 보상 누락은 종류마다 한 줄로 묶는다 — 통째로 비어 있는 종류(아직 안 채운 신규 출석 등)가
		// 콘솔을 30줄씩 덮으면 진짜 구멍 하나를 못 본다.
		private static void validate(DailyBonusType type)
		{
			System.Text.StringBuilder missing = new System.Text.StringBuilder();

			IReadOnlyList<Table_DailyBonus.Row> list = DailyBonusRules.GetDays(type);
			for (int i = 0; i < list.Count; i++)
			{
				Table_DailyBonus.Row row = list[i];
				if (row.DayCount != i + 1)
				{
					Debug.LogWarning($"[DailyBonusCatalog] {type} 의 일차가 이어지지 않는다 — {i + 1} 이어야 할 자리에 {row.DayCount} 가 있다.");
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
				Debug.LogWarning($"[DailyBonusCatalog] {type} 의 RewardGroupID 가 비어 있다 — {missing}일차. DailyBonus 엑셀을 채워야 한다.");
			}
		}
	}
}
