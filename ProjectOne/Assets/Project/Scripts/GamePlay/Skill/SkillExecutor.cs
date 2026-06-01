using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Audio;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Skill
{
	// Table_SkillInfo 행 한 개를 받아 CastingType 별로 발동 흐름을 분기하는 정적 디스패처
	// - Instant/None : 모션 재생 → MotionEffectTime 후 효과 적용
	// - OnHit        : IsOnHitTrigger 공격 적중 시 SkillContainer 가 확률 발동 (흐름은 Instant 와 동일)
	// - Casting      : CastingParam 초 후 발동
	// - Passive      : 등록 시 SkillContainer 가 ApplyPassive 로 즉시 상시 적용
	// 지연은 코루틴이 아니라 SkillContainer.Tick 의 예약 큐로 처리한다 (동시 다수 스킬 부하 회피).
	public static class SkillExecutor
	{
		public static void Execute(SkillInfo id, UnitBase caster)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
			if (row == null)
			{
				Debug.LogError($"[SkillExecutor] Skill 행 없음 — Skill:{id}");
				return;
			}

			// 스킬 실행 시작 알림 — 모든 실행 경로(Instant/OnHit/Casting/None) 단일 지점
			EventManager.Instance.Publish(new SkillCastEvent(caster, id));

			switch (row.CastingType)
			{
				case SkillCastingTypes.Casting:
					// CastingParam 초 후 모션+효과 발동
					caster.SkillContainer.Schedule(row.CastingParam, id, PendingKind.PlayAndApply);
					break;
				case SkillCastingTypes.Passive:
					// 보통 Register 에서 ApplyPassive 로 처리됨 — 직접 호출 시 안전망으로 효과만 즉시 적용
					ApplyEffects(row, id, caster);
					break;
				default:
					// Instant / OnHit / None
					PlayAndApply(row, id, caster);
					break;
			}
		}

		// Passive — 모션/딜레이/쿨타임/프록 없이 효과만 상시 적용
		public static void ApplyPassive(SkillInfo id, UnitBase caster)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
			if (row == null)
			{
				Debug.LogError($"[SkillExecutor] Skill 행 없음 — Skill:{id}");
				return;
			}

			ApplyEffects(row, id, caster);
		}

		// SkillContainer.Tick 예약 디스패치 진입점 — Casting 대기 종료 후
		public static void RunPlayAndApply(SkillInfo id, UnitBase caster)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
			if (row == null)
			{
				return;
			}

			PlayAndApply(row, id, caster);
		}

		// SkillContainer.Tick 예약 디스패치 진입점 — MotionEffectTime 대기 종료 후
		public static void RunApplyEffects(SkillInfo id, UnitBase caster)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
			if (row == null)
			{
				return;
			}

			ApplyEffects(row, id, caster);
		}

		// 모션 트리거 후 MotionEffectTime 만큼 지연했다가 효과 적용 (0 이면 즉시)
		static void PlayAndApply(Table_SkillInfo.Row row, SkillInfo id, UnitBase caster)
		{
			PlayMotion(row, caster);

			// 시전 캐릭터 위치에서 스킬 VFX 1회 출력
			if (string.IsNullOrEmpty(row.SkillVFX) == false)
			{
				VFXManager.Instance.PlayOneShot(row.SkillVFX, caster.transform);
			}

			// 스킬 SFX 1회 재생 (시전 시마다)
			if (string.IsNullOrEmpty(row.SkillSFX) == false)
			{
				AudioManager.Instance.PlaySFX(row.SkillSFX);
			}

			if (row.MotionEffectTime > 0f)
			{
				caster.SkillContainer.Schedule(row.MotionEffectTime, id, PendingKind.ApplyEffects);
			}
			else
			{
				ApplyEffects(row, id, caster);
			}
		}

		// ScanType 기반 후보 1회 산출 후 StartEffect → Effect_0..4 → FinishEffect 순 적용
		static void ApplyEffects(Table_SkillInfo.Row row, SkillInfo id, UnitBase caster)
		{
			// scanned 는 TargetResolver 내부 _scratch 직참조 →
			// 아래 ApplyIfSet 들 사이에 다른 TargetResolver.ScanByType 호출이 끼면 안 됨.
			List<UnitBase> scanned = TargetResolver.ScanByType(row.ScanType, row.ScanParam1, row.ScanParam2, caster);
			// OnHit 트리거 판정 — 스캔 영역에 "적"이 1명 이상일 때만 적중으로 본다.
			// (캐스터 자신·아군은 모든 ScanType 후보에 포함되므로 faction 으로 걸러야 함)
			// 효과 적용 루프 앞에서 미리 산출 — 루프 내 FilterByApplyTarget 호출이 _filtered 를 덮어쓰므로.
			bool hasEnemyTarget = TargetResolver.FilterByApplyTarget(scanned, SkillApplyTarget.Enemy, caster).Count > 0;

			ApplyIfSet(row.StartEffect,  caster, id, scanned);
			ApplyIfSet(row.Effect_0,     caster, id, scanned);
			ApplyIfSet(row.Effect_1,     caster, id, scanned);
			ApplyIfSet(row.Effect_2,     caster, id, scanned);
			ApplyIfSet(row.Effect_3,     caster, id, scanned);
			ApplyIfSet(row.Effect_4,     caster, id, scanned);
			ApplyIfSet(row.FinishEffect, caster, id, scanned);

			// OnHit 트리거: IsOnHitTrigger 공격이 1명 이상 적중하면 OnHit 스킬 확률 발동 (공격당 1회)
			// 위 apply 루프가 동기적으로 끝난 뒤 호출 → 재귀 ScanByType 이 _scratch 를 덮어써도 안전
			if (row.IsOnHitTrigger == true && hasEnemyTarget == true && caster.SkillContainer != null)
			{
				caster.SkillContainer.TriggerOnHitSkills();
			}
		}

		static void ApplyIfSet(SkillEffect effectId, UnitBase caster, SkillInfo skillId, List<UnitBase> scanned)
		{
			if (effectId == SkillEffect.None)
			{
				return;
			}

			SkillEffectApplier.Apply(effectId, caster, skillId, scanned);
		}

		// MotionName 비어있으면 모션 없음
		static void PlayMotion(Table_SkillInfo.Row row, UnitBase caster)
		{
			if (string.IsNullOrEmpty(row.MotionName) == true)
			{
				return;
			}

			UnitAnimator anim = caster.GetComponent<UnitAnimator>();
			if (anim != null)
			{
				anim.PlayMotion(row.MotionName);
			}
		}
	}
}
