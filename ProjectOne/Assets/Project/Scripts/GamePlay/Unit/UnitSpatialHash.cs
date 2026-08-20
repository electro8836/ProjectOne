using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Unit
{
	// 유닛 공간 해시 (순수 C# 클래스 — UnitManager 가 소유하고 FixedUpdate 에서 Rebuild).
	// 충돌/분리 핫 루프가 전체 유닛을 brute-force 순회하던 O(N²) 을, 인접 셀 후보만 보는 O(N·k) 로 낮춘다.
	// CachedPos 기준으로 구축 — 기존 충돌 쿼리가 이미 CachedPos(프레임 시작 위치)로 동작하므로 의미가 일관된다.
	public sealed class UnitSpatialHash
	{
		// 셀 크기 — 유닛 반경 ~0.3, 충돌 영향 반경(반경합) < 0.6 이라 3×3 조회로 충분히 커버.
		private const float _cellSize = 1f;
		private const float _inverseCellSize = 1f / _cellSize;

		// 셀 좌표(int x, y) → 그 셀에 속한 유닛 목록. 셀 List 는 Clear 후 재사용해 GC 를 피한다.
		private readonly Dictionary<long, List<UnitBase>> _cells = new Dictionary<long, List<UnitBase>>(256);

		// 셀 좌표 두 개를 long 하나로 패킹 (딕셔너리 키)
		private static long CellKey(int x, int y)
		{
			return ((long)x << 32) ^ (uint)y;
		}

		private static int CellCoord(float v)
		{
			return Mathf.FloorToInt(v * _inverseCellSize);
		}

		// 프레임당 1회 — 모든 유닛을 CachedPos 가 속한 셀 하나에 등록.
		// IsDead 는 거르지 않는다(호출자가 기존처럼 판정 루프에서 걸러냄).
		public void Rebuild(IReadOnlyList<UnitBase> all)
		{
			// 기존 셀 List 는 비우기만 하고 보존 (딕셔너리/리스트 재할당 금지)
			Dictionary<long, List<UnitBase>>.Enumerator e = _cells.GetEnumerator();
			while (e.MoveNext())
			{
				e.Current.Value.Clear();
			}

			for (int i = 0; i < all.Count; i++)
			{
				UnitBase u = all[i];
				if (u == null)
				{
					continue;
				}

				Vector2 p = u.CachedPos;
				long key  = CellKey(CellCoord(p.x), CellCoord(p.y));

				List<UnitBase> list;
				if (_cells.TryGetValue(key, out list) == false)
				{
					list = new List<UnitBase>(16);
					_cells[key] = list;
				}

				list.Add(u);
			}
		}

		// center 주변 3×3 셀의 유닛을 result 에 수집 (호출자 버퍼 재사용 — 람다 금지 규칙).
		// 한 유닛은 한 셀에만 속하므로 중복 없음.
		public void Query(Vector2 center, List<UnitBase> result)
		{
			result.Clear();

			int cx = CellCoord(center.x);
			int cy = CellCoord(center.y);

			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					long key = CellKey(cx + dx, cy + dy);
					List<UnitBase> list;
					if (_cells.TryGetValue(key, out list) == false)
					{
						continue;
					}

					for (int i = 0; i < list.Count; i++)
					{
						result.Add(list[i]);
					}
				}
			}
		}
	}
}
