using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Unit
{
	// 유닛 일괄 구동 로직 (순수 C# 클래스 — UnitContainer 가 소유하고 Fixed/LateUpdate 에서 구동).
	// - 모든 유닛의 위치/반경을 프레임당 1회 캐시 → 핫 루프의 네이티브 브릿지 호출 제거
	// - 몬스터 분리(Separation) 벡터를 pair-wise 로 한 번에 배치 계산
	// - 모든 유닛의 ManualTick 을 단일 루프로 구동 → 개별 MonoBehaviour.LateUpdate 콜백 오버헤드 제거
	// 캐시 데이터는 각 유닛 필드(CachedPos 등)에 보관 — 이 클래스는 구동만 하고 상태를 떠안지 않는다.
	public sealed class UnitSimulator
	{
		// 모든 유닛의 위치/반경을 캐시 (transform.position / collider.radius 를 유닛당 1회만 호출)
		public void RefreshCache(IReadOnlyList<UnitBase> all)
		{
			for (int i = 0; i < all.Count; i++)
			{
				UnitBase u = all[i];
				if (u != null)
				{
					u.RefreshFrameCache();
				}
			}
		}

		// 모든 유닛 ManualTick 일괄 구동
		public void TickAll(IReadOnlyList<UnitBase> all, float dt)
		{
			for (int i = 0; i < all.Count; i++)
			{
				UnitBase u = all[i];
				if (u != null)
				{
					u.ManualTick(dt);
				}
			}
		}

		// 몬스터 분리 벡터 배치 계산 — pair-wise(i<j) 로 각 쌍을 1회만 계산해 양쪽에 반대로 누적.
		// 결과는 각 유닛의 CachedSeparation 에 기록. (MonsterApproachBehavior 가 읽음)
		public void ComputeSeparations(IReadOnlyList<UnitBase> monsters)
		{
			int count = monsters.Count;

			// 1) 전부 초기화
			for (int i = 0; i < count; i++)
			{
				UnitBase m = monsters[i];
				if (m != null)
				{
					m.CachedSeparation = Vector2.zero;
				}
			}

			// 2) pair-wise 반발 누적 (기존 MonsterApproachBehavior.ComputeSeparation 공식 이식)
			for (int i = 0; i < count; i++)
			{
				UnitBase a = monsters[i];
				if (a == null || a.IsDead == true)
				{
					continue;
				}

				Vector2 aPos = a.CachedPos;
				float aRad = a.CachedRadius;

				for (int j = i + 1; j < count; j++)
				{
					UnitBase b = monsters[j];
					if (b == null || b.IsDead == true)
					{
						continue;
					}

					Vector2 away = aPos - b.CachedPos;
					float distSqr = away.sqrMagnitude;
					float r = aRad + b.CachedRadius;
					if (distSqr < 1e-6f || distSqr > r * r)
					{
						continue;
					}

					float dist = Mathf.Sqrt(distSqr);
					Vector2 push = away / dist * (1f - dist / r);

					// a 는 b 에서 멀어지는 방향, b 는 그 반대
					a.CachedSeparation += push;
					b.CachedSeparation -= push;
				}
			}
		}
	}
}
