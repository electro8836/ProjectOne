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
	// - Instant/None  : 모션 재생 → MotionEffectTime 후 효과 적용
	// - OnHitCaster   : IsOnHitTrigger 공격 적중 시 SkillContainer 가 확률 발동 (흐름은 Instant 와 동일, 캐스터 기준 1회)
	// - OnHitTarget   : IsOnHitTrigger 공격 적중 시 피격자별 확률 판정 → ExecuteAtTarget 로 피격자 위치 즉발
	// - Casting       : CastingParam 초 후 발동
	// - Passive       : 등록 시 SkillContainer 가 ApplyPassive 로 즉시 상시 적용
	// 지연은 코루틴이 아니라 SkillContainer.Tick 의 예약 큐로 처리한다 (동시 다수 스킬 부하 회피).
	public static class SkillExecutor
	{
		// OnHit 트리거 시 SkillContainer 로 넘길 명중 적 목록 — apply 루프가 _filtered 를 덮어쓰므로 선스냅샷용
		static readonly List<UnitBase> _onHitEnemies = new List<UnitBase>(8);

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

			// 스킬 실행 시작 알림 — 모든 실행 경로(Instant/OnHit/Casting/None/코드) 단일 지점
			EventManager.Instance.Publish(new SkillCastEvent(caster, id));

			// 코드 스킬(behavior) 우선 — registry 에 등록된 스킬이면 SkillContainer 에 위임.
			// 코드 스킬은 테이블 효과 칼럼을 읽지 않고 자체 로직으로 동작한다 (필요 시 내부에서 SkillEffectApplier 직접 호출).
			if (caster.SkillContainer != null)
			{
				ISkillBehavior behavior = SkillBehaviorRegistry.Create(id);
				if (behavior != null)
				{
					caster.SkillContainer.BeginBehavior(id, behavior);
					return;
				}
			}

			switch (row.CastingType)
			{
				case SkillCastingTypes.Casting:
					// 캐스팅 시간(CastingParam) 안에서 진행 — 진입 모션은 IsCasting(Bool) 전이가 담당.
					// 종료 MotionEffectTime 초 전에 공격모션, 종료 시점에 효과 발동 (BeginCasting 2단계 예약)
					caster.SkillContainer.BeginCasting(row.CastingParam, row.MotionEffectTime, id);
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

		// SkillContainer.Tick 예약 디스패치 진입점 — 캐스팅 공격모션 단계(종료 MotionEffectTime 초 전)
		// 공격모션(MotionNames[1]) + VFX + SFX 만 재생, 효과는 캐스팅 종료 시점 RunApplyEffects 에서
		public static void RunCastAttackMotion(SkillInfo id, UnitBase caster)
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

			PlayMotionVfxSfx(row, caster, 1);
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

		// 모션 트리거 후 MotionEffectTime 만큼 지연했다가 효과 적용 (0 이면 즉시) — 비캐스팅(Instant/None/OnHit) 전용
		static void PlayAndApply(Table_SkillInfo.Row row, SkillInfo id, UnitBase caster)
		{
			// 비캐스팅 발동 모션 — 첫 번째 모션
			PlayMotionVfxSfx(row, caster, 0);

			if (row.MotionEffectTime > 0f)
			{
				caster.SkillContainer.Schedule(row.MotionEffectTime, id, PendingKind.ApplyEffects);
			}
			else
			{
				ApplyEffects(row, id, caster);
			}
		}

		// 발동 모션 트리거 + 스킬 VFX/SFX 1회 재생 (효과 적용은 별도)
		static void PlayMotionVfxSfx(Table_SkillInfo.Row row, UnitBase caster, int motionIndex)
		{
			PlayMotionAt(row, caster, motionIndex);

			// 시전 캐릭터에 스킬 VFX 1회 출력 — 앵커가 Center 면 충돌체 중심만큼 띄워 부착
			if (string.IsNullOrEmpty(row.SkillVFX) == false)
			{
				Vector3 offset = (row.SkillVFXAnchor == SkillAnchorType.Center) ? (Vector3)(caster.HitCenter - (Vector2)caster.transform.position) : Vector3.zero;
				VFXManager.Instance.PlayOneShot(row.SkillVFX, caster.transform, offset);
			}

			// 스킬 SFX 1회 재생 (시전 시마다)
			if (string.IsNullOrEmpty(row.SkillSFX) == false)
			{
				AudioManager.Instance.PlaySFX(row.SkillSFX);
			}
		}

		// ScanType 기반 후보 1회 산출(캐스터 기준) 후 효과 적용 + IsOnHitTrigger 면 OnHit 스킬 발동
		static void ApplyEffects(Table_SkillInfo.Row row, SkillInfo id, UnitBase caster)
		{
			// scanned 는 TargetResolver 내부 _scratch 직참조 →
			// 아래 ApplyIfSet 들 사이에 다른 TargetResolver.ScanByType 호출이 끼면 안 됨.
			List<UnitBase> scanned = TargetResolver.ScanByType(row.ScanType, row.ScanParam1, row.ScanParam2, caster);

			// OnHit 트리거 판정 — 스캔 영역에 "적"이 1명 이상일 때만 적중으로 본다.
			// (캐스터 자신·아군은 모든 ScanType 후보에 포함되므로 faction 으로 걸러야 함)
			// 효과 적용 루프 앞에서 미리 산출 — 루프 내 FilterByApplyTarget 호출이 _filtered 를 덮어쓰므로.
			// OnHitTarget 이 피격자별로 발동할 수 있게 적 목록 자체를 안정 버퍼에 스냅샷한다.
			bool triggerOnHit = false;
			if (row.IsOnHitTrigger == true && caster.SkillContainer != null)
			{
				List<UnitBase> enemies = TargetResolver.FilterByApplyTarget(scanned, SkillApplyTarget.Enemy, caster);
				_onHitEnemies.Clear();
				for (int i = 0; i < enemies.Count; i++)
				{
					_onHitEnemies.Add(enemies[i]);
				}

				triggerOnHit = _onHitEnemies.Count > 0;
			}

			ApplyAllEffects(row, id, caster, scanned);

			// OnHit 트리거: IsOnHitTrigger 공격이 1명 이상 적중하면 OnHit 스킬 발동.
			// 위 apply 루프가 동기적으로 끝난 뒤 호출 → 재귀 ScanByType 이 _scratch 를 덮어써도 안전.
			if (triggerOnHit == true)
			{
				caster.SkillContainer.TriggerOnHitSkills(_onHitEnemies);
			}
		}

		// 피격자(target) 위치 기준으로 스캔/효과를 즉발 — OnHitTarget 전용. 모션/딜레이/SkillCastEvent 없음.
		public static void ExecuteAtTarget(SkillInfo id, UnitBase caster, UnitBase target)
		{
			if (caster == null || caster.IsDead == true || target == null || target.IsDead == true)
			{
				return;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
			if (row == null)
			{
				Debug.LogError($"[SkillExecutor] Skill 행 없음 — Skill:{id}");
				return;
			}

			// SkillCastEvent 는 발행하지 않는다 — 그 이벤트의 인디케이터는 캐스터 위치에 그려져 피격자 위치와 어긋남.
			// 대신 피격자 위치/방향을 담은 SkillProcAtTargetEvent 를 발행해 인디케이터가 피격자 위치에 표시되게 한다.
			Vector2 center = target.HitCenter;
			Vector2 toTarget = center - (Vector2)caster.HitCenter;
			Vector2 facing = (toTarget.sqrMagnitude > 1E-06f) ? toTarget.normalized : Vector2.right;
			EventManager.Instance.Publish(new SkillProcAtTargetEvent(caster, id, center, facing));

			// 스킬 VFX 1회 — 피격자 위치(월드)에 출력
			if (string.IsNullOrEmpty(row.SkillVFX) == false)
			{
				VFXManager.Instance.PlayOneShot(row.SkillVFX, new Vector3(center.x, center.y, target.transform.position.z));
			}

			// 스킬 SFX 1회
			if (string.IsNullOrEmpty(row.SkillSFX) == false)
			{
				AudioManager.Instance.PlaySFX(row.SkillSFX);
			}

			// 피격자 위치/방향으로 스캔 중심을 옮겨 효과 적용 (faction 기준은 여전히 caster)
			List<UnitBase> scanned = TargetResolver.ScanByType(row.ScanType, row.ScanParam1, row.ScanParam2, caster, useOverride: true, centerOverride: center, facingOverride: facing);
			ApplyAllEffects(row, id, caster, scanned);
		}

		// StartEffect → Effect_0..4 → FinishEffect 순 적용 — scanned 후보에 각 효과의 ApplyTarget 필터링은 내부에서 수행
		static void ApplyAllEffects(Table_SkillInfo.Row row, SkillInfo id, UnitBase caster, List<UnitBase> scanned)
		{
			ApplyIfSet(row.StartEffect,  caster, id, scanned);
			ApplyIfSet(row.Effect_0,     caster, id, scanned);
			ApplyIfSet(row.Effect_1,     caster, id, scanned);
			ApplyIfSet(row.Effect_2,     caster, id, scanned);
			ApplyIfSet(row.Effect_3,     caster, id, scanned);
			ApplyIfSet(row.Effect_4,     caster, id, scanned);
			ApplyIfSet(row.FinishEffect, caster, id, scanned);
		}

		static void ApplyIfSet(SkillEffect effectId, UnitBase caster, SkillInfo skillId, List<UnitBase> scanned)
		{
			if (effectId == SkillEffect.None)
			{
				return;
			}

			SkillEffectApplier.Apply(effectId, caster, skillId, scanned);
		}

		// MotionNames[index] 모션 재생 — 인덱스 범위 밖이거나 비어있으면 모션 없음
		static void PlayMotionAt(Table_SkillInfo.Row row, UnitBase caster, int index)
		{
			if (row.MotionNames == null || index < 0 || index >= row.MotionNames.Length)
			{
				return;
			}

			string motionName = row.MotionNames[index];
			if (string.IsNullOrEmpty(motionName) == true)
			{
				return;
			}

			UnitAnimator anim = caster.GetComponent<UnitAnimator>();
			if (anim != null)
			{
				anim.PlayMotion(motionName);
			}
		}
	}
}
