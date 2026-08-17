using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Audio;
using ProjectOne.Combat;
using ProjectOne.Buff;
using ProjectOne.Projectile;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Utils;

namespace ProjectOne.Skill
{
	// SkillEffect 행 한 개를 대상 목록에 적용한다. 9종 EffectType 분기가 전부 여기 모여 있다.
	//
	// - 탐색은 이미 SkillExecutor 가 마쳤다. 여기서는 EffectOrigin 으로 "누구에게" 만 정한다.
	// - ChainEffectIDs 는 이 효과가 적중/성공했을 때만 연쇄한다 (설계 5.6).
	// - OnHitTrigger 가 TRUE 인 효과만 OnHit/OnCrit/흡혈을 발동시킨다 (설계 5.7).
	public static class SkillEffectApplier
	{
		// EffectOrigin 해석 결과를 담는 버퍼 — 재진입 시 덮어쓰지 않도록 깊이별로 나눠 쓴다.
		static readonly List<UnitBase>[] _originBuffers = createOriginBuffers();

		static List<UnitBase>[] createOriginBuffers()
		{
			List<UnitBase>[] buffers = new List<UnitBase>[SkillConstants.CHAIN_DEPTH_LIMIT + 1];
			for (int i = 0; i < buffers.Length; i++)
			{
				buffers[i] = new List<UnitBase>(8);
			}

			return buffers;
		}

		// depth 는 ChainEffectIDs 재귀 깊이다. 0에서 시작한다.
		public static void Apply(SkillEffect effectId, UnitBase caster, EDT.Skill skillId, List<UnitBase> scanned, int depth)
		{
			if (effectId == SkillEffect.None || caster == null)
			{
				return;
			}

			if (depth > SkillConstants.CHAIN_DEPTH_LIMIT)
			{
				Debug.LogError($"[SkillEffectApplier] 연쇄 깊이 초과 — Effect:{effectId} (순환 참조 의심)");
				return;
			}

			// 리졸브 사본에서 먼저 찾는다 — 모디파이어가 반영된 값을 써야 한다 (설계 11.1).
			// 연쇄(ChainEffectIDs)로 딸려온 효과는 스킬의 효과 목록에 없으므로 테이블로 폴백한다.
			Table_SkillEffect.Row row = null;
			ResolvedSkill resolved = caster.Resolve(skillId);
			if (resolved != null)
			{
				row = resolved.FindEffect(effectId);
			}

			if (row == null)
			{
				row = Table_SkillEffect.Get(effectId);
			}

			if (row == null)
			{
				Debug.LogError($"[SkillEffectApplier] SkillEffect 행 없음 — Effect:{effectId}");
				return;
			}

			// 한 칸 밀려 적은 데이터를 잡는 유일한 장치
			SkillParamCatalog.WarnUndefinedSlots(row);

			List<UnitBase> targets = resolveOrigin(row.EffectOrigin, caster, scanned, depth);

			bool succeeded = false;
			switch (row.EffectType)
			{
				case SkillEffectTypes.Damage:
					succeeded = applyDamage(row, caster, skillId, targets);
					break;
				case SkillEffectTypes.Heal:
					succeeded = applyHeal(row, caster, targets);
					break;
				case SkillEffectTypes.Buff:
					succeeded = applyBuff(row, caster, skillId, targets);
					break;
				case SkillEffectTypes.StatChange:
					succeeded = applyStatChange(row, targets);
					break;
				case SkillEffectTypes.Projectile:
					succeeded = applyProjectile(row, caster, skillId, targets);
					break;
				case SkillEffectTypes.Summon:
					// TODO(STEP 13) — 소환물 시스템 미구현.
					Debug.LogWarning($"[SkillEffectApplier] Summon 효과는 아직 구현되지 않았습니다 — Effect:{row.ID}");
					break;
				case SkillEffectTypes.Force:
					succeeded = applyForce(row, caster, targets);
					break;
				case SkillEffectTypes.CooldownReduce:
					succeeded = applyCooldownReduce(row, caster);
					break;
				case SkillEffectTypes.BuffConsume:
					succeeded = applyBuffConsume(row, targets);
					break;
				default:
					Debug.LogError($"[SkillEffectApplier] 알 수 없는 EffectType — Effect:{row.ID} Type:{row.EffectType}");
					break;
			}

			playEffectPresentation(row, targets);

			// 적중 개념이 없는 효과는 "성공"을 적중으로 간주한다 (설계 5.6).
			if (succeeded == true)
			{
				applyChain(row, caster, skillId, scanned, depth);
			}
		}

		// 지정한 위치를 중심으로 반경 안의 대상을 새로 탐색해 효과를 적용한다.
		// 투사체 착탄 폭발(Projectile.ExplodeRadius)이 쓰는 경로다 — 스킬의 탐색 결과와 무관하다.
		public static void ApplyAtPosition(SkillEffect effectId, UnitBase caster, EDT.Skill skillId, Vector3 center, float radius)
		{
			if (effectId == SkillEffect.None || caster == null)
			{
				return;
			}

			List<UnitBase> scanned = TargetResolver.ScanByType(SkillScanTypes.Circle, radius, 0f, caster,
				useOverride: true, centerOverride: new Vector2(center.x, center.y), facingOverride: Vector2.right);

			// 착탄은 적에게만 적용한다 — 탐색 결과에는 아군·자신도 섞여 있다.
			List<UnitBase> enemies = TargetResolver.FilterByApplyTarget(scanned, SkillApplyTarget.Enemy, caster);
			Apply(effectId, caster, skillId, enemies, 0);
		}

		// ── EffectOrigin 해석 (설계 2.6) ──────────────────────────────

		static List<UnitBase> resolveOrigin(SkillEffectOrigin origin, UnitBase caster, List<UnitBase> scanned, int depth)
		{
			List<UnitBase> buffer = _originBuffers[depth];
			buffer.Clear();

			switch (origin)
			{
				case SkillEffectOrigin.Caster:
				case SkillEffectOrigin.Owner:
					// 소환물 체계가 없는 동안 Owner 는 시전자와 같다 (STEP 13에서 분리).
					buffer.Add(caster);
					break;

				case SkillEffectOrigin.Target:
				case SkillEffectOrigin.Victim:
				case SkillEffectOrigin.Attacker:
				case SkillEffectOrigin.Location:
					// 탐색 결과를 그대로 쓴다. Attacker/Victim 의 구분은 트리거 경로가 이미 대상을 좁혀 넘긴다.
					if (scanned != null)
					{
						for (int i = 0; i < scanned.Count; i++)
						{
							if (scanned[i] != null && scanned[i].IsDead == false)
							{
								buffer.Add(scanned[i]);
							}
						}
					}

					break;

				default:
					Debug.LogError($"[SkillEffectApplier] EffectOrigin 이 None 입니다 — 효과가 아무에게도 적용되지 않습니다.");
					break;
			}

			return buffer;
		}

		// ── 타입별 적용 ───────────────────────────────────────────────

		static bool applyDamage(Table_SkillEffect.Row row, UnitBase caster, EDT.Skill skillId, List<UnitBase> targets)
		{
			DamageParams p;
			if (SkillEffectParams.TryParseDamage(row, out p) == false || targets.Count == 0)
			{
				return false;
			}

			bool anyHit = false;
			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];
				if (target == null || target.IsDead == true || target == caster)
				{
					continue;
				}

				// 다단히트는 ①②의 예외 — 의도된 반복이다 (설계 5.8).
				// HitInterval 은 고정값이며 공속의 영향을 받지 않는다.
				for (int hit = 0; hit < p.HitCount; hit++)
				{
					if (hit == 0)
					{
						anyHit |= dealDamage(caster, target, skillId, row, p);
					}
					else
					{
						// TODO — 다단히트 지연 발동. 현재는 첫 타만 적용하고 나머지는 즉시 처리한다.
						anyHit |= dealDamage(caster, target, skillId, row, p);
					}
				}
			}

			return anyHit;
		}

		static bool dealDamage(UnitBase caster, UnitBase target, EDT.Skill skillId, Table_SkillEffect.Row row, in DamageParams p)
		{
			DamageCalculator.Result result = DamageCalculator.Calculate(caster, target, p.ScaleStat, p.Ratio, p.FlatValue, row.OnHitTrigger);
			if (result.IsAvoided == true)
			{
				return false;
			}

			DamageInfo info = default(DamageInfo);
			info.Attacker = caster;
			info.Damage = result.Damage;
			info.HitPoint = target.HitCenter;
			info.IsCritical = result.IsCritical;
			info.SkillID = (int)skillId;
			info.TriggersOnHit = row.OnHitTrigger;

			IDamageable damageable = target as IDamageable;
			if (damageable != null)
			{
				damageable.TakeDamage(info);
			}

			// 흡혈 — OnHitTrigger 가 TRUE 인 효과만 (설계 10.1 [9])
			if (result.LifeOnHit > 0f && caster.Vitals != null)
			{
				caster.Vitals.ModifyHp(result.LifeOnHit);
			}

			// 온히트 계열 발동 — 다단히트에는 TRUE 를 넣지 않는 것이 데이터 규약이다 (설계 5.7)
			if (row.OnHitTrigger == true && caster.SkillContainer != null)
			{
				caster.SkillContainer.TriggerOnHit();
				if (result.IsCritical == true)
				{
					caster.SkillContainer.TriggerOnCrit();
				}
			}

			return true;
		}

		static bool applyHeal(Table_SkillEffect.Row row, UnitBase caster, List<UnitBase> targets)
		{
			HealParams p;
			if (SkillEffectParams.TryParseHeal(row, out p) == false || targets.Count == 0)
			{
				return false;
			}

			int amount = DamageCalculator.CalculateHeal(caster, p.ScaleStat, p.Ratio, p.FlatValue);
			if (amount <= 0)
			{
				return false;
			}

			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];
				if (target == null || target.IsDead == true || target.Vitals == null)
				{
					continue;
				}

				// TODO — TickCount > 1 인 지속 회복은 버프 틱으로 표현하는 편이 자연스럽다. STEP 13 이후 정리.
				target.Vitals.ModifyHp(amount);
			}

			return true;
		}

		static bool applyBuff(Table_SkillEffect.Row row, UnitBase caster, EDT.Skill skillId, List<UnitBase> targets)
		{
			BuffParams p;
			if (SkillEffectParams.TryParseBuff(row, out p) == false || targets.Count == 0)
			{
				return false;
			}

			bool anyApplied = false;
			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];
				if (target == null || target.IsDead == true || target.BuffContainer == null)
				{
					continue;
				}

				// Chance 판정을 통과했을 때만 연쇄가 이어진다 (설계 5.6)
				if (Random.value >= p.Chance)
				{
					continue;
				}

				target.BuffContainer.Apply(p.RefID, p.Duration, p.StackMax, caster, skillId);
				anyApplied = true;
			}

			return anyApplied;
		}

		static bool applyStatChange(Table_SkillEffect.Row row, List<UnitBase> targets)
		{
			StatChangeParams p;
			if (SkillEffectParams.TryParseStatChange(row, out p) == false || targets.Count == 0)
			{
				return false;
			}

			bool anyApplied = false;
			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];
				if (target == null || target.Stats == null)
				{
					continue;
				}

				// Base 레이어에는 장비·노드·버프가 값을 넣을 수 없다 (설계 3.4)
				if (target.Stats.CanAddModifier(p.StatDetailID) == false)
				{
					Debug.LogError($"[SkillEffectApplier] StatChange 대상이 Base 레이어이거나 정의가 없음 — Effect:{row.ID} {p.StatDetailID}");
					continue;
				}

				// TODO — Duration > 0 인 한시 변경은 버프로 표현해야 회수 경로가 생긴다.
				// 지금은 영구(Duration=0) 만 적용하고 한시는 경고한다.
				if (p.Duration > 0f)
				{
					Debug.LogWarning($"[SkillEffectApplier] 지속시간이 있는 StatChange 는 Buff 로 표현하세요 — Effect:{row.ID}");
					continue;
				}

				target.Stats.AddModifier(p.StatDetailID, p.Value, row.ID.ToString());
				anyApplied = true;
			}

			return anyApplied;
		}

		static bool applyProjectile(Table_SkillEffect.Row row, UnitBase caster, EDT.Skill skillId, List<UnitBase> targets)
		{
			ProjectileParams p;
			if (SkillEffectParams.TryParseProjectile(row, out p) == false)
			{
				return false;
			}

			Table_Projectile.Row projectile = Table_Projectile.Get(p.RefID);
			if (projectile == null)
			{
				Debug.LogError($"[SkillEffectApplier] Projectile 행 없음 — Effect:{row.ID} RefID:{p.RefID}");
				return false;
			}

			UnitBase target = (targets.Count > 0) ? targets[0] : null;
			Vector2 origin = caster.HitCenter;
			Vector2 baseDir = resolveFacing(caster, target, origin);

			// Count 개를 Angle 간격으로 분산 발사한다. Interval 은 연속 발사 간격(0=동시).
			float half = (p.Count - 1) * 0.5f;
			for (int i = 0; i < p.Count; i++)
			{
				float angle = (p.Angle != 0f) ? (i - half) * p.Angle : 0f;
				Vector2 dir = (angle != 0f) ? (Vector2)(Quaternion.Euler(0f, 0f, angle) * baseDir) : baseDir;

				ProjectileData data = default(ProjectileData);
				data.direction = new Vector3(dir.x, dir.y, 0f);
				data.startPos = new Vector3(origin.x, origin.y, caster.transform.position.z);
				data.caster = caster;
				data.skillId = skillId;
				data.hitEffect = projectile.HitEffectID_1;
				data.target = target;
				data.hitRadius = projectile.ExplodeRadius;

				// TODO(STEP 13) — Interval(연속 발사)·SpeedRate·Pierce·Accel 은 투사체 재작업에서 붙인다.
				ProjectileManager.Instance.Launch(projectile.PrefabPath, data);
			}

			return true;
		}

		static bool applyForce(Table_SkillEffect.Row row, UnitBase caster, List<UnitBase> targets)
		{
			ForceParams p;
			if (SkillEffectParams.TryParseForce(row, out p) == false || targets.Count == 0)
			{
				return false;
			}

			bool anyApplied = false;
			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];
				if (target == null || target.IsDead == true || target.Mover == null || target == caster)
				{
					continue;
				}

				Vector2 away = target.HitCenter - caster.HitCenter;
				if (away.sqrMagnitude < 1e-6f)
				{
					away = Vector2.right;
				}

				Vector2 dir = away.normalized;
				if (p.ForceType == ForceType.Pull)
				{
					dir = -dir;
				}

				target.Mover.AddImpulse(dir * p.Power);
				anyApplied = true;
			}

			return anyApplied;
		}

		static bool applyCooldownReduce(Table_SkillEffect.Row row, UnitBase caster)
		{
			CooldownReduceParams p;
			if (SkillEffectParams.TryParseCooldownReduce(row, out p) == false || caster.SkillContainer == null)
			{
				return false;
			}

			caster.SkillContainer.ReduceCooldown(p.TargetSkillID, p.Ratio, p.FlatValue);
			return true;
		}

		// 스택 소모에 성공했을 때만 연쇄가 이어진다 — 조건 분기의 게이트다 (설계 5.9).
		static bool applyBuffConsume(Table_SkillEffect.Row row, List<UnitBase> targets)
		{
			BuffConsumeParams p;
			if (SkillEffectParams.TryParseBuffConsume(row, out p) == false || targets.Count == 0)
			{
				return false;
			}

			bool anyConsumed = false;
			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];
				if (target == null || target.BuffContainer == null)
				{
					continue;
				}

				if (target.BuffContainer.TryConsume(p.RefID, p.Count) == true)
				{
					anyConsumed = true;
				}
			}

			return anyConsumed;
		}

		// ── 연쇄 / 연출 ───────────────────────────────────────────────

		static void applyChain(Table_SkillEffect.Row row, UnitBase caster, EDT.Skill skillId, List<UnitBase> scanned, int depth)
		{
			if (row.ChainEffectIDs == null || row.ChainEffectIDs.Length == 0)
			{
				return;
			}

			for (int i = 0; i < row.ChainEffectIDs.Length; i++)
			{
				SkillEffect chained = row.ChainEffectIDs[i];
				if (chained == SkillEffect.None)
				{
					continue;
				}

				// 연쇄 효과는 EffectTime 을 무시하고 부모 발동 시점에 즉시 실행된다 (설계 5.6).
				Apply(chained, caster, skillId, scanned, depth + 1);
			}
		}

		// 타격 연출 — Additive 정책. 누적을 허용하고 각자 수명대로 소멸한다 (설계 4.8).
		static void playEffectPresentation(Table_SkillEffect.Row row, List<UnitBase> targets)
		{
			if (string.IsNullOrEmpty(row.EffectVFX) == false)
			{
				for (int i = 0; i < targets.Count; i++)
				{
					UnitBase target = targets[i];
					if (target == null)
					{
						continue;
					}

					Vector2 center = target.HitCenter;
					VFXManager.Instance.PlayOneShot(row.EffectVFX, new Vector3(center.x, center.y, target.transform.position.z));
				}
			}

			if (string.IsNullOrEmpty(row.EffectSFX) == false && targets.Count > 0)
			{
				AudioManager.Instance.PlaySFX(row.EffectSFX);
			}
		}

		static Vector2 resolveFacing(UnitBase caster, UnitBase target, Vector2 origin)
		{
			if (target != null)
			{
				Vector2 toTarget = target.HitCenter - origin;
				if (toTarget.sqrMagnitude > 1e-6f)
				{
					return toTarget.normalized;
				}
			}

			if (caster.Mover != null && caster.Mover.Facing.sqrMagnitude > 1e-6f)
			{
				return caster.Mover.Facing;
			}

			return Vector2.right;
		}
	}
}
