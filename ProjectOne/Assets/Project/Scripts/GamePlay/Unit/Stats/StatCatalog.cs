using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Unit.Stats
{
	// 스탯 정적 조회 캐시 — Table_StatDetail / Table_Stat 을 런타임 조회용 인덱스로 1회 변환한다.
	//
	// StatContainer 가 매 접근마다 테이블을 뒤지지 않게 하는 것이 목적이고,
	// 파트맵을 테이블에서 만들기 때문에 "테이블 로드 이후"에 반드시 Build() 가 불려야 한다.
	// 부트 순서: TableBootLoader.LoadAllAsync → StatCatalog.Build() (BootState 가 호출).
	public static class StatCatalog
	{
		// StatDetail → (소속 스탯, 레이어)
		private static readonly Dictionary<StatDetail, StatPart> _partMap = new Dictionary<StatDetail, StatPart>();

		// 스탯별 클램프 — 값이 있을 때만 적용한다(0 = 제한 없음, 기반테이블 2.3).
		private struct Clamp
		{
			public float min;
			public float max;
		}

		private static readonly Dictionary<Stat, Clamp> _clamps = new Dictionary<Stat, Clamp>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		// 테이블 로드 직후 1회 호출. 재호출하면 인덱스를 다시 만든다(에디터 리로드 대비).
		public static void Build()
		{
			_partMap.Clear();
			_clamps.Clear();

			Dictionary<StatDetail, Table_StatDetail.Row> details = Table_StatDetail.All();
			Dictionary<StatDetail, Table_StatDetail.Row>.Enumerator de = details.GetEnumerator();
			while (de.MoveNext() == true)
			{
				Table_StatDetail.Row row = de.Current.Value;
				if (row.ID == StatDetail.None)
				{
					continue;
				}

				if (row.StatID == Stat.None || row.StatDetailType == StatDetailTypes.None)
				{
					Debug.LogError($"[StatCatalog] StatDetail 행이 소속 스탯 또는 레이어를 지정하지 않음: {row.ID}");
					continue;
				}

				_partMap[row.ID] = new StatPart(row.StatID, row.StatDetailType);
			}

			Dictionary<Stat, Table_Stat.Row> stats = Table_Stat.All();
			Dictionary<Stat, Table_Stat.Row>.Enumerator se = stats.GetEnumerator();
			while (se.MoveNext() == true)
			{
				Table_Stat.Row row = se.Current.Value;
				if (row.ID == Stat.None)
				{
					continue;
				}

				Clamp c;
				c.min = row.MinValue;
				c.max = row.MaxValue;
				_clamps[row.ID] = c;
			}

			_built = true;

			// 데이터 누락은 조용히 넘어가면 전 스탯이 0이 되므로 여기서 바로 드러낸다.
			if (_partMap.Count != details.Count)
			{
				Debug.LogError($"[StatCatalog] 파트맵 수({_partMap.Count})가 StatDetail 행 수({details.Count})와 다릅니다. 테이블을 확인하세요.");
			}

			Debug.Log($"[StatCatalog] 구축 완료 — StatDetail:{_partMap.Count} Stat:{_clamps.Count}");
		}

		public static bool TryGetPart(StatDetail detail, out StatPart part)
		{
			if (_built == false)
			{
				Debug.LogError("[StatCatalog] Build() 이전에 조회했습니다. 부트 순서를 확인하세요.");
				part = default(StatPart);
				return false;
			}

			return _partMap.TryGetValue(detail, out part);
		}

		// 최종 스탯에 클램프를 1회 적용한다. Min/Max 가 0이면 그 방향의 제한이 없다.
		public static float ApplyClamp(Stat stat, float value)
		{
			Clamp c;
			if (_clamps.TryGetValue(stat, out c) == false)
			{
				return value;
			}

			if (c.min != 0f && value < c.min)
			{
				value = c.min;
			}

			if (c.max != 0f && value > c.max)
			{
				value = c.max;
			}

			return value;
		}
	}
}
