using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Audio;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Combat;
using ProjectOne.Buff;
using ProjectOne.Utils;

namespace ProjectOne.Skill
{
	// SkillEffect 한 개를 caster + 산출 대상에 적용
	// - SkillExecutor: 스킬 실행 시 Apply(...) 호출
	// - BuffRuntime  : 버프 부착/주기 시 ApplyOnBuff(...) 호출 (Self 해석 기준이 caster=owner 가 되도록 분리)
	public static class SkillEffectApplier
	{
		// 버프 경로(ApplyOnBuff) 전용 — owner 1명만 담아 ApplyInternal 의 scanned 자리에 전달
		static readonly List<UnitBase> _buffScratch = new List<UnitBase>(1);

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
			if (p.CoefStat != StatTypes.None && source != null && source.Stats != null)
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

				// 방어력(DEF/MDEF) 퍼센트 감소 — 대상별로 적용
				int finalDamage = ApplyDefense(rawDamage, type, targets[i], source);

				DamageInfo info = new DamageInfo
				{
					Attacker = source,
					Damage = finalDamage,
					DamageType = type,
					HitPoint = targets[i].transform.position,
					KnockbackDir = Vector2.zero,
					KnockbackPower = 0f,
					IsCritical = false,
					SkillID = (int)skillId
				};
				dmg.TakeDamage(in info);
			}
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
			StatTypes defStat = magical ? StatTypes.MDEF : StatTypes.DEF;
			StatTypes penStat = magical ? StatTypes.Pen_Magic : StatTypes.Pen_Physical;

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
