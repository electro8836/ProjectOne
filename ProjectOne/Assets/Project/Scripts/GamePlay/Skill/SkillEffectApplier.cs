using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Audio;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Combat;
using ProjectOne.Buff;
using ProjectOne.Utils;
using ProjectOne.Event;

namespace ProjectOne.Skill
{
	// SkillEffect 한 개를 caster + 산출 대상에 적용
	// - SkillExecutor: 스킬 실행 시 Apply(...) 호출
	// - BuffRuntime  : 버프 부착/주기 시 ApplyOnBuff(...) 호출 (Self 해석 기준이 caster=owner 가 되도록 분리)
	public static class SkillEffectApplier
	{
		// 버프 경로(ApplyOnBuff) 전용 — owner 1명만 담아 ApplyInternal 의 scanned 자리에 전달
		static readonly List<UnitBase> _buffScratch = new List<UnitBase>(1);

		// 가드브레이크 강제 적용(SKILL_BREAK) 중 플래그 — 이 동안의 피격은 게이지를 다시 깎지 않음(재발동 방지)
		static bool _applyingGuardBreak;

		public static void Apply(SkillEffect effectId, UnitBase caster, SkillInfo skillId, List<UnitBase> scanned)
		{
			ApplyInternal(effectId, caster, caster, skillId, hostBuff: null, scanned: scanned);
		}

		public static void ApplyOnBuff(SkillEffect effectId, UnitBase owner, UnitBase source, BuffRuntime hostBuff)
		{
			// 버프 컨텍스트: Self 효과는 버프 소유자(owner)에 적용
			// source 는 데미지 어트리뷰션용
			// ScanType 없음 → owner 단일 후보로 scanned 구성
			_buffScratch.Clear();
			if (owner != null)
			{
				_buffScratch.Add(owner);
			}

			ApplyInternal(effectId, owner, source, SkillInfo.None, hostBuff, _buffScratch);
		}

		static void ApplyInternal(SkillEffect effectId, UnitBase caster, UnitBase damageSource, SkillInfo skillId, BuffRuntime hostBuff, List<UnitBase> scanned)
		{
			if (effectId == SkillEffect.None)
			{
				return;
			}

			Table_SkillEffect.Row row = Table_SkillEffect.Get(effectId);
			if (row == null)
			{
				Debug.LogError($"[SkillEffectApplier] Effect 행 없음 — Effect:{effectId}");
				return;
			}

			List<UnitBase> targets = TargetResolver.FilterByApplyTarget(scanned, row.ApplyTarget, caster);

			// 효과가 적용되는 각 대상 유닛의 공격자 방향 외곽에 효과 VFX 1회 월드 소환
			if (string.IsNullOrEmpty(row.EffectVFX) == false)
			{
				for (int i = 0; i < targets.Count; i++)
				{
					if (targets[i] != null)
					{
						playEffectVFX(row.EffectVFX, targets[i], damageSource, row.EffectType);
					}
				}
			}

			// 효과 SFX(피격음) — 대상별로 요청하되 AudioManager throttle 이 윈도우당 상한에서 컷.
			// 100명이 동시 피격돼도 사운드는 _sfxThrottleMaxPerWindow 개까지만 재생된다.
			if (string.IsNullOrEmpty(row.EffectSFX) == false)
			{
				for (int i = 0; i < targets.Count; i++)
				{
					if (targets[i] != null)
					{
						AudioManager.Instance.PlaySFXThrottled(row.EffectSFX);
					}
				}
			}

			switch (row.EffectType)
			{
				case SkillEffectTypes.Damage:
					ApplyDamage(row, targets, damageSource, skillId);
					break;
				case SkillEffectTypes.ActivateBuff:
					ApplyActivateBuff(row, targets, damageSource);
					break;
				case SkillEffectTypes.DeactivateBuff:
					ApplyDeactivateBuff(row, targets);
					break;
				case SkillEffectTypes.IncreaseAttribute:
					ApplyAttribute(row, targets, signMul: +1f, hostBuff);
					break;
				case SkillEffectTypes.DecreaseAttribute:
					ApplyAttribute(row, targets, signMul: -1f, hostBuff);
					break;
				case SkillEffectTypes.ActivateAura:
					// TODO: 오라 시스템 미구현
					Debug.Log($"[SkillEffectApplier] ActivateAura stub — Effect:{effectId}");
					break;
				default:
					break;
			}
		}

		// Damage 효과는 공격자→적 방향으로 적 외곽에 배치, 그 외 효과는 적 중심(Center)에 고정 소환
		static void playEffectVFX(string address, UnitBase target, UnitBase attacker, SkillEffectTypes effectType)
		{
			Vector2 center = target.HitCenter;
			float z = target.transform.position.z;

			// Damage 외 효과(버프/스탯 등)는 방향성 없이 대상 중심에 출력
			if (effectType != SkillEffectTypes.Damage)
			{
				VFXManager.Instance.PlayOneShot(address, new Vector3(center.x, center.y, z));
				return;
			}

			// 공격자 없음/자기 자신(버프 self) → 방향 없음 → 중심
			if (attacker == null || attacker == target)
			{
				VFXManager.Instance.PlayOneShot(address, new Vector3(center.x, center.y, z));
				return;
			}

			Vector2 toAttacker = (Vector2)attacker.HitCenter - center;
			float dist = toAttacker.magnitude;
			if (dist <= Mathf.Epsilon)
			{
				VFXManager.Instance.PlayOneShot(address, new Vector3(center.x, center.y, z));
				return;
			}

			// 공격자 반지름을 뺀 접촉 지점을 [중심(0) ~ 적 반지름] 으로 클램프
			// - 원거리: dist-attackerR 큼 → 적 반지름(외곽)
			// - 밀착:   dist-attackerR 작음 → 중심 쪽
			float offset = Mathf.Clamp(dist - attacker.Radius, 0f, target.Radius);
			Vector2 pos = center + (toAttacker / dist) * offset;
			VFXManager.Instance.PlayOneShot(address, new Vector3(pos.x, pos.y, z));
		}

		static void ApplyDamage(Table_SkillEffect.Row row, List<UnitBase> targets, UnitBase source, SkillInfo skillId)
		{
			DamageParams p;
			if (SkillEffectParams.TryParseDamage(row, out p) == false)
			{
				return;
			}

			float coef = 0f;
			if (p.CoefStat != StatInfo.None && source != null && source.Stats != null)
			{
				coef = source.Stats.GetStat(p.CoefStat) * p.CoefValue;
			}

			int rawDamage = Mathf.RoundToInt(p.BaseDamage + coef);
			DamageType type = ResolveDamageType(row.DamageType);

			for (int i = 0; i < targets.Count; i++)
			{
				IDamageable dmg = targets[i] as IDamageable;
				if (dmg == null)
				{
					continue;
				}

				// 크리티컬 판정 — 대상별로 1회 굴림
				bool isCritical;
				bool isSuperCritical;
				float critMul = RollCritical(source, out isCritical, out isSuperCritical);
				int critDamage = Mathf.RoundToInt(rawDamage * critMul);

				// 방어력(DEF/MDEF) 퍼센트 감소 — 대상별로 적용
				int afterDefense = ApplyDefense(critDamage, type, targets[i], source);

				// 받는 데미지 감소/증폭 — DEF 다음 단계: dam × (1 - DamageReduction) × DamageTakenAmp
				int finalDamage = ApplyIncomingMultipliers(afterDefense, targets[i]);

				// 넉백 힘 — 시전자 KnockBack 스탯 × 비율, 대상 KnockBackResist 로 퍼센트 감소
				Vector2 kbDir;
				float kbPower;
				ResolveKnockback(p.KnockbackRatio, source, targets[i], out kbDir, out kbPower);

				DamageInfo info = new DamageInfo
				{
					Attacker = source,
					Damage = finalDamage,
					DamageType = type,
					HitPoint = targets[i].transform.position,
					KnockbackDir = kbDir,
					KnockbackPower = kbPower,
					IsCritical = isCritical,
					IsSuperCritical = isSuperCritical,
					SkillID = (int)skillId
				};
				dmg.TakeDamage(in info);

				// 브레이크 게이지 데미지 = 공격자 BreakDamage × EffectParam_4 (HP 데미지와 무관)
				// 브레이크 상태 중(IsBreak)에는 게이지 추가 감소 없음
				if (_applyingGuardBreak == false && p.BreakDamageRatio != 0f && source != null && source.Stats != null
					&& targets[i] != null && targets[i].Vitals != null && targets[i].Stats != null && targets[i].IsDead == false
					&& targets[i].Vitals.IsBreak == false && targets[i].Stats.GetStat(StatInfo.BreakGage) > 0f)
				{
					float breakDmg = source.Stats.GetStat(StatInfo.BreakDamage) * p.BreakDamageRatio;
					if (breakDmg > 0f)
					{
						targets[i].Vitals.ModifyBreakGage(-breakDmg);
						if (targets[i].Vitals.IsBreakGageZero == true)
						{
							TriggerGuardBreak(targets[i], source);
						}
					}
				}
			}
		}

		// 받는 데미지 감소/증가 적용 — DEF/MDEF 방어 다음 단계.
		// dam × (1 - DamageReduction) × (1 + DamageTakenAmp)
		// - 두 스탯 모두 100 스케일(30 = 30%) → 사용 시 /100. 0이면 증감 없음.
		// - DamageReduction: Clamp01 (100 이상이면 데미지 0)
		static int ApplyIncomingMultipliers(int damage, UnitBase target)
		{
			if (target == null || target.Stats == null)
			{
				return damage;
			}

			float reduction = Mathf.Clamp01(target.Stats.GetStat(StatInfo.DamageReduction) / 100f);
			float amp = target.Stats.GetStat(StatInfo.DamageTakenAmp) / 100f;
			int result = Mathf.RoundToInt(damage * (1f - reduction) * (1f + amp));
			return Mathf.Max(0, result);
		}

		// 가드브레이크 발동 — 브레이크 게이지가 0이 된 victim 에게 호출.
		// 브레이크 상태(IsBreak) 진입 후 SKILL_BREAK 강제 적용 — 스턴/디버프(ActivateBuff)와 가드브레이크 데미지
		static void TriggerGuardBreak(UnitBase victim, UnitBase attacker)
		{
			victim.Vitals.SetBreakGage(0f);

			bool prev = _applyingGuardBreak;
			_applyingGuardBreak = true;
			try
			{
				ApplyForced(SkillInfo.SKILL_BREAK, victim, attacker);
			}
			finally
			{
				_applyingGuardBreak = prev;
			}

			// 브레이크 UI 등 외부 구독자에게 발동 알림
			EventManager.Instance.Publish(new GuardBreakTriggeredEvent(victim, attacker));
		}

		// 스킬의 모든 효과를 단일 대상(target)에 강제 적용 — ScanType 무시, source 로 데미지 귀속.
		// ApplyOnBuff 경로 재사용 → 효과행은 ApplyTarget=Self 전제 (대상이 owner 후보로 통과해야 함).
		public static void ApplyForced(SkillInfo skill, UnitBase target, UnitBase source)
		{
			if (target == null)
			{
				return;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(skill);
			if (row == null)
			{
				Debug.LogError($"[SkillEffectApplier] ApplyForced — Skill 행 없음: {skill}");
				return;
			}

			ApplyOnBuff(row.StartEffect, target, source, null);
			ApplyOnBuff(row.Effect_0,     target, source, null);
			ApplyOnBuff(row.Effect_1,     target, source, null);
			ApplyOnBuff(row.Effect_2,     target, source, null);
			ApplyOnBuff(row.Effect_3,     target, source, null);
			ApplyOnBuff(row.Effect_4,     target, source, null);
			ApplyOnBuff(row.FinishEffect, target, source, null);
		}

		// 넉백 힘/방향 산출.
		// - 힘 = 시전자 KnockBack 스탯 × |ratio| × (1 - clamp01(대상 KnockBackResist/100))
		// - 방향 = 시전자→대상(밀어내기), ratio 음수면 반대(당기기)
		// - ratio 0 / 시전자 없음 / 넉백 스탯 0 / 두 위치 동일 → 넉백 없음(power=0)
		static void ResolveKnockback(float ratio, UnitBase source, UnitBase target, out Vector2 dir, out float power)
		{
			dir = Vector2.zero;
			power = 0f;

			if (ratio == 0f || source == null || source.Stats == null || target == null)
			{
				return;
			}

			float casterKnock = source.Stats.GetStat(StatInfo.KnockBack);
			if (casterKnock <= 0f)
			{
				return;
			}

			Vector2 delta = (Vector2)target.HitCenter - (Vector2)source.HitCenter;
			if (delta.sqrMagnitude <= Mathf.Epsilon)
			{
				return;
			}

			dir = delta.normalized;
			if (ratio < 0f)
			{
				dir = -dir;
			}

			float resist = target.Stats != null ? target.Stats.GetStat(StatInfo.KnockBackResist) : 0f;
			float reduction = Mathf.Clamp01(resist / 100f);
			power = casterKnock * Mathf.Abs(ratio) * (1f - reduction);
		}

		// 공격자 스탯 기준 크리티컬 판정.
		// - CritRate(0~100%)로 1차 검사, 크리면 SuperCritRate로 2차 검사
		// - 배율: 크리 시 CritDam/100, 초크리면 (CritDam+SuperCritDam)/100 (비율 스탯이라 100=1.0배)
		// - 비크리면 multiplier=1, isCritical=false, isSuperCritical=false
		static float RollCritical(UnitBase attacker, out bool isCritical, out bool isSuperCritical)
		{
			isCritical = false;
			isSuperCritical = false;
			if (attacker == null || attacker.Stats == null)
			{
				return 1f;
			}

			float critRate = attacker.Stats.GetStat(StatInfo.CritRate);
			if (Random.value * 100f >= critRate)
			{
				return 1f;
			}

			isCritical = true;
			float critDam = attacker.Stats.GetStat(StatInfo.CritDam);

			float superRate = attacker.Stats.GetStat(StatInfo.SuperCritRate);
			if (Random.value * 100f < superRate)
			{
				isSuperCritical = true;
				float superDam = attacker.Stats.GetStat(StatInfo.SuperCritDam);
				return (critDam + superDam) / 100f;
			}

			return critDam / 100f;
		}

		// 테이블 SkillDamageType → 런타임 DamageType (Pure=감소 없는 고정 데미지, None=물리 기본)
		static DamageType ResolveDamageType(SkillDamageType t)
		{
			switch (t)
			{
				case SkillDamageType.Magical: return DamageType.Magical;
				case SkillDamageType.Pure:    return DamageType.True;
				default:                      return DamageType.Physical;
			}
		}

		// 퍼센트 감소 + 관통: 유효방어 = max(0, DEF - Pen). 감소율 = clamp(유효방어/100, 0~1).
		// True(Pure)는 감소 없음. 방어/관통 모두 동일 퍼센트 스케일(100=100%)이라 단순 차감.
		static int ApplyDefense(int rawDamage, DamageType type, UnitBase target, UnitBase attacker)
		{
			if (type == DamageType.True || target == null || target.Stats == null)
			{
				return rawDamage;
			}

			bool magical = type == DamageType.Magical;
			StatInfo defStat = magical ? StatInfo.MDEF : StatInfo.DEF;
			StatInfo penStat = magical ? StatInfo.Pen_Magic : StatInfo.Pen_Physical;

			float def = target.Stats.GetStat(defStat);
			float pen = 0f;
			if (attacker != null && attacker.Stats != null)
			{
				pen = attacker.Stats.GetStat(penStat);
			}

			float effective = Mathf.Max(0f, def - pen);        // 0 밑으로 안 내려감
			float reduction = Mathf.Clamp01(effective / 100f); // 상한 100%
			int result = Mathf.RoundToInt(rawDamage * (1f - reduction));
			return Mathf.Max(0, result);
		}

		static void ApplyActivateBuff(Table_SkillEffect.Row row, List<UnitBase> targets, UnitBase source)
		{
			ActivateBuffParams p;
			if (SkillEffectParams.TryParseActivateBuff(row, out p) == false)
			{
				return;
			}

			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase t = targets[i];
				if (t == null || t.BuffContainer == null)
				{
					continue;
				}

				t.BuffContainer.Apply(p.BuffID, p.Duration, p.Interval, source);
			}
		}

		static void ApplyDeactivateBuff(Table_SkillEffect.Row row, List<UnitBase> targets)
		{
			DeactivateBuffParams p;
			if (SkillEffectParams.TryParseDeactivateBuff(row, out p) == false)
			{
				return;
			}

			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase t = targets[i];
				if (t == null || t.BuffContainer == null)
				{
					continue;
				}

				t.BuffContainer.Remove(p.BuffID);
			}
		}

		static void ApplyAttribute(Table_SkillEffect.Row row, List<UnitBase> targets, float signMul, BuffRuntime hostBuff)
		{
			AttributeParams p;
			if (SkillEffectParams.TryParseAttribute(row, out p) == false)
			{
				return;
			}

			// 컨벤션: Target=Self 만 허용
			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase t = targets[i];
				if (t == null || t.Stats == null)
				{
					continue;
				}

				StatModifier handle;
				try
				{
					handle = t.Stats.AddModifier(p.AttrType, p.Value * signMul);
				}
				catch (System.ArgumentException e)
				{
					Debug.LogError($"[SkillEffectApplier] AddModifier 실패 — Effect:{row.ID}, Stat:{p.AttrType} ({e.Message})");
					continue;
				}

				if (hostBuff != null)
				{
					hostBuff.RegisterModifier(handle);
				}

				// hostBuff == null 이면 영구 적용 (제거 핸들 없음) — Skill 직접 슬롯의 패시브 용도
			}
		}
	}
}
