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

		// 모든 유닛 이동(UnitMover) 일괄 고정 스텝 구동 — per-MonoBehaviour FixedUpdate 콜백 제거.
		// 캐시/공간해시 Rebuild 이후에 호출해야 무버가 최신 상태를 본다.
		public void TickMovers(IReadOnlyList<UnitBase> all, float dt)
		{
			for (int i = 0; i < all.Count; i++)
			{
				UnitBase u = all[i];
				if (u != null && u.Mover != null)
				{
					u.Mover.ManualFixedTick(dt);
				}
			}
		}

		// 분리 인지 반경 배수 — 콜라이더 반경합의 이 배수 거리부터 미리 반발해, 부딪히기 전에 간격을 벌린다.
		// 몬스터 0.3×1.6≈0.48, 보스 포함 0.4×1.6≈0.64 — 셀(1.0) 미만이라 공간 해시 3×3 조회로 그대로 커버.
		private const float _separationRangeMul = 1.6f;

		// 분리 벡터 시간 평활화 계수(EMA) — 프레임 간 분리 변동을 흡수해 뒷줄 떨림을 억제. 작을수록 더 부드럽다.
		private const float _separationSmoothing = 0.3f;

		// 인접 후보 조회 버퍼 — 분리 계산용(GC 회피로 재사용)
		private readonly List<UnitBase> _separationBuffer = new List<UnitBase>(32);

		// 몬스터 분리 벡터 배치 계산 — 공간 해시로 인접 후보만 조회해 O(M·k).
		// 각 몬스터가 자기 기준 반발을 누적(self 1방향) — 결과는 pair-wise 와 동등.
		// 결과는 각 유닛의 CachedSeparation 에 기록. (MonsterApproachBehavior 가 읽음)
		public void ComputeSeparations(IReadOnlyList<UnitBase> monsters, UnitSpatialHash hash)
		{
			int count = monsters.Count;

			for (int i = 0; i < count; i++)
			{
				UnitBase a = monsters[i];
				if (a == null)
				{
					continue;
				}

				if (a.IsDead == true)
				{
					a.CachedSeparation = Vector2.zero;
					continue;
				}

				Vector2 aPos = a.CachedPos;
				float   aRad = a.CachedRadius;
				Vector2 sum  = Vector2.zero;

				// 인접 셀 후보만 조회 — 셀(1.0) > 반발 반경(반경합×1.6 ~0.64)이라 3×3 으로 커버
				hash.Query(aPos, _separationBuffer);
				for (int j = 0; j < _separationBuffer.Count; j++)
				{
					UnitBase b = _separationBuffer[j];
					if (b == null || b == a || b.IsDead == true || b.GetUnitType() != UnitType.Monster)
					{
						continue;
					}

					Vector2 away = aPos - b.CachedPos;
					float distSqr = away.sqrMagnitude;
					float r = (aRad + b.CachedRadius) * _separationRangeMul;
					if (distSqr < 1e-6f || distSqr > r * r)
					{
						continue;
					}

					float dist = Mathf.Sqrt(distSqr);
					sum += away / dist * (1f - dist / r);
				}

				a.CachedSeparation = Vector2.Lerp(a.CachedSeparation, sum, _separationSmoothing);
			}
		}
	}
}
