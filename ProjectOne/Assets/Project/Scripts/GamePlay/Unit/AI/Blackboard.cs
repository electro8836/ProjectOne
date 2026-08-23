using UnityEngine;
using EDT;

namespace ProjectOne.Unit.AI
{
	// AI 의사결정 공유 상태 (POCO). 이동형 AI(몬스터/PVP)가 타겟/앵커를 저장하는 용도.
	// 히어로 자동스킬 AI 는 사용하지 않는다.
	public sealed class Blackboard
	{
		public UnitBase Target;

		// 스폰 원점 — 리쉬(복귀) 판정과 필드 리젠의 기준이다 (몬스터 설계 5장).
		// "플레이어와 얼마나 멀어지면 포기"가 아니라 "자기 자리에서 얼마나 벗어나면 복귀"다.
		public Vector2 Anchor;

		// ── Monster 테이블 AI 파라미터 ───────────────────────────────
		// 스폰 시 1회 주입된다. 데이터가 없으면 아래 기본값으로 동작한다.

		public AggroType Aggro = AggroType.Aggressive;
		public float DetectRange = DefaultDetectRange;
		public float LeashRange = DefaultLeashRange;

		// 테이블에 값이 없을 때의 폴백. 0으로 두면 아무도 탐지하지 못해 몬스터가 멈춰 선다.
		public const float DefaultDetectRange = 8f;
		public const float DefaultLeashRange = 12f;

		// 비선공(Neutral)이 피격으로 각성했는가. 한 번 깨면 리쉬 복귀 전까지 유지된다.
		public bool Provoked;

		// 스폰 원점으로 복귀하는 중 — 복귀 중에는 타겟을 다시 잡지 않는다.
		public bool Returning;

		// ── 소환물 전용 ───────────────────────────────────────────────
		// 소환물은 앵커가 고정 좌표가 아니라 "주인"이다. Anchor 를 매 틱 주인 위치로 갱신해
		// TickLeash 를 그대로 재사용한다 (Table_Summon.ReturnDistance 가 LeashRange 다).

		public UnitBase SummonOwner;

		public float FollowDistance;

		// 타겟 후보 유형 — 몬스터는 히어로를, 소환물은 몬스터를 노린다.
		public UnitType TargetUnitType = UnitType.Hero;

		public void ApplyAI(Table_Monster.Row row)
		{
			if (row == null)
			{
				Aggro = AggroType.Aggressive;
				DetectRange = DefaultDetectRange;
				LeashRange = DefaultLeashRange;
				return;
			}

			Aggro = (row.AggroType != AggroType.None) ? row.AggroType : AggroType.Aggressive;
			DetectRange = (row.DetectRange > 0f) ? row.DetectRange : DefaultDetectRange;
			LeashRange = (row.LeashRange > 0f) ? row.LeashRange : DefaultLeashRange;
		}

		// 소환물용 — 소환물은 AggroType 이 없다. 언제나 선공이고 대상은 몬스터다.
		public void ApplySummon(Table_Summon.Row row)
		{
			Aggro = AggroType.Aggressive;
			TargetUnitType = UnitType.Monster;
			DetectRange = DefaultDetectRange;

			if (row == null)
			{
				LeashRange = DefaultLeashRange;
				FollowDistance = DefaultFollowDistance;
				return;
			}

			LeashRange = (row.ReturnDistance > 0f) ? row.ReturnDistance : DefaultLeashRange;
			FollowDistance = (row.FollowDistance > 0f) ? row.FollowDistance : DefaultFollowDistance;
		}

		public const float DefaultFollowDistance = 3f;

		// 스폰/리스폰 시 초기화. 앵커는 그 자리에 고정된다.
		public void ResetForSpawn(Vector2 spawnOrigin)
		{
			Anchor = spawnOrigin;
			Target = null;
			Provoked = false;
			Returning = false;
		}

		// 선공형이거나 이미 각성했으면 탐지한다 (설계 2장 AggroType).
		public bool CanAcquireTarget
		{
			get { return Aggro == AggroType.Aggressive || Provoked == true; }
		}
	}
}
