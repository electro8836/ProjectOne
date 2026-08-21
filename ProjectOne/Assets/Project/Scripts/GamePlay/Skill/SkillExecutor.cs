using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Audio;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Skill
{
	// Table_Skill 행 한 개를 받아 CastingType 별로 발동 흐름을 분기하는 정적 디스패처.
	//
	// 탐색은 스킬이 한 번만 한다 (설계 5.1). 그 결과를 효과별 지연 예약에 스냅샷으로 실어 보낸다.
	// 지연은 코루틴이 아니라 SkillContainer.Tick 의 예약 큐로 처리한다 (동시 다수 스킬 부하 회피).
	public static class SkillExecutor
	{
		// useSpeed 는 호출자가 계산해 넘긴다 — 평타만 공속을 받고 나머지는 1.0 이다.
		public static void Execute(EDT.Skill id, UnitBase caster, float useSpeed)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			// 리졸브를 거친 사본을 쓴다 — 테이블 원본은 절대 읽고 쓰지 않는다 (설계 11.2).
			ResolvedSkill resolved = caster.Resolve(id);
			if (resolved == null || resolved.IsValid == false)
			{
				Debug.LogError($"[SkillExecutor] EDT.Skill 행 없음 — EDT.Skill:{id}");
				return;
			}

			// Replace 모디파이어가 스킬 자체를 교체했을 수 있다. 이후 경로는 교체된 ID 를 쓴다.
			id = resolved.Id;
			Table_Skill.Row row = resolved.Row;

			// 스킬 실행 시작 알림 — 모든 실행 경로의 단일 지점
			EventManager.Instance.Publish(new SkillCastEvent(caster, id));

			// 코드 스킬(behavior) 우선 — 등록된 스킬이면 SkillContainer 에 위임하고 테이블 효과는 읽지 않는다.
			// 단 캐스팅형은 즉시 시작하지 않고 캐스팅 흐름(선딜·취소)을 거친 뒤 시작한다.
			if (caster.SkillContainer != null && row.CastingType != SkillCastingTypes.Casting)
			{
				ISkillBehavior behavior = SkillBehaviorRegistry.Create(id);
				if (behavior != null)
				{
					caster.SkillContainer.BeginBehavior(id, behavior);
					return;
				}
			}

			if (row.CastingType == SkillCastingTypes.Casting)
			{
				// 캐스팅 — CastingParam(초) 만큼 차단한 뒤 효과가 나간다.
				// 캐스팅 시간은 공속의 영향을 받지 않는다 (설계 4.7).
				caster.SkillContainer.BeginCasting(row.CastingParam, id);
				playSkillPresentation(row, caster, useSpeed);
				scheduleEffects(resolved, caster, useSpeed, row.CastingParam);
				return;
			}

			playSkillPresentation(row, caster, useSpeed);
			scheduleEffects(resolved, caster, useSpeed, 0f);
		}

		// Passive — 모션/딜레이/쿨타임 없이 효과만 상시 적용
		public static void ApplyPassive(EDT.Skill id, UnitBase caster)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			ResolvedSkill resolved = caster.Resolve(id);
			if (resolved == null || resolved.IsValid == false)
			{
				Debug.LogError($"[SkillExecutor] EDT.Skill 행 없음 — EDT.Skill:{id}");
				return;
			}

			scheduleEffects(resolved, caster, 1f, 0f);
		}

		// SkillContainer 예약 디스패치 진입점 — 효과 1개를 대상 스냅샷에 적용한다.
		public static void RunEffect(EDT.Skill skillId, SkillEffect effectId, UnitBase caster, List<UnitBase> targets)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			SkillEffectApplier.Apply(effectId, caster, skillId, targets, 0);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 탐색 1회 → 효과별 지연 예약. extraDelay 는 캐스팅 시간처럼 사이클 앞에 붙는 고정 지연이다.
		//
		// 효과 목록은 리졸브 결과가 소유한다 — 테이블은 2슬롯이지만 Append 모디파이어로 늘어날 수 있다.
		static void scheduleEffects(ResolvedSkill resolved, UnitBase caster, float useSpeed, float extraDelay)
		{
			if (caster.SkillContainer == null)
			{
				return;
			}

			Table_Skill.Row row = resolved.Row;

			// scanned 는 TargetResolver 내부 버퍼 직참조 — ScheduleEffect 가 즉시 복사해 스냅샷을 만든다.
			List<UnitBase> scanned = TargetResolver.ScanByType(row.ScanType, row.ApplyTarget, row.ScanRange, row.ScanParam, caster);

			SkillRuntime rt = caster.SkillContainer.GetRuntime(resolved.Id);
			float actionTime = (rt != null) ? rt.GetActionTime(useSpeed) : 0f;

			for (int i = 0; i < resolved.Effects.Count; i++)
			{
				Table_SkillEffect.Row effect = resolved.Effects[i];
				float delay = extraDelay + actionTime * effect.EffectTime;
				caster.SkillContainer.ScheduleEffect(delay, resolved.Id, effect.ID, scanned);
			}
		}

		// 시전 연출 — Replace 정책. 새 사이클이 시작되면 이전 것을 즉시 밀어낸다 (설계 4.8).
		static void playSkillPresentation(Table_Skill.Row row, UnitBase caster, float useSpeed)
		{
			playAnim(row, caster, useSpeed);

			if (string.IsNullOrEmpty(row.SkillVFX) == false)
			{
				// 충돌체 중심에 부착 — 신규 스키마에 앵커 컬럼이 없어 항상 중심 기준이다.
				Vector3 offset = (Vector3)(caster.HitCenter - (Vector2)caster.transform.position);
				VFXManager.Instance.PlayOneShot(row.SkillVFX, caster.transform, offset);
			}

			if (string.IsNullOrEmpty(row.SkillSFX) == false)
			{
				AudioManager.Instance.PlaySFX(row.SkillSFX);
			}
		}

		// AnimName 재생. 재생 속도는 공속에서 파생되며 클램프된다 (설계 4.2).
		static void playAnim(Table_Skill.Row row, UnitBase caster, float useSpeed)
		{
			if (string.IsNullOrEmpty(row.AnimName) == true)
			{
				return;
			}

			UnitAnimator anim = caster.GetComponent<UnitAnimator>();
			if (anim == null)
			{
				return;
			}

			anim.PlayMotion(row.AnimName);
		}
	}
}
