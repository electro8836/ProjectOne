using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Summons;
using ProjectOne.Audio;
using ProjectOne.Combat;
using ProjectOne.Buff;
using ProjectOne.Event;
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
		// hasCenter/center 는 좌표 고정형(EffectOrigin=Location) 전용 — 연출을 대상마다가 아니라 그 좌표에서 1회 낸다.
		// buffOwner 는 버프가 부여한 효과일 때만 넘어온다 — StatChange 모디파이어 회수를 버프 수명에 맡긴다.
		public static void Apply(SkillEffect effectId, UnitBase caster, EDT.Skill skillId, List<UnitBase> scanned, int depth,
			bool hasCenter = false, Vector2 center = default(Vector2), BuffRuntime buffOwner = null)
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

			Table_SkillEffect.Row row = resolveRow(effectId, caster, skillId);
			if (row == null)
			{
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
					succeeded = applyHeal(row, caster, skillId, targets);
					break;
				case SkillEffectTypes.Buff:
					succeeded = applyBuff(row, caster, skillId, targets);
					break;
				case SkillEffectTypes.StatChange:
					succeeded = applyStatChange(row, caster, targets, buffOwner);
					break;
				case SkillEffectTypes.Projectile:
					succeeded = applyProjectile(row, caster, skillId, targets);
					break;
				case SkillEffectTypes.Summon:
					succeeded = applySummon(row, caster, targets);
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

			// 타격 연출은 실제로 효과가 적용된 대상에만 나가야 한다.
			// applyDamage/applyForce 는 시전자를 건너뛰므로 연출도 같은 규칙을 따른다.
			// 반대로 자힐·자버프는 시전자가 정당한 대상이라 제외하면 안 된다.
			UnitBase vfxExcluded = null;
			if (row.EffectType == SkillEffectTypes.Damage || row.EffectType == SkillEffectTypes.Force)
			{
				vfxExcluded = caster;
			}

			playEffectPresentation(row, targets, vfxExcluded, hasCenter, center);

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

			// 착탄은 적에게만 적용한다 — 탐색이 진영을 걸러 준다.
			List<UnitBase> scanned = TargetResolver.ScanByType(SkillScanTypes.Circle, SkillApplyTarget.Enemy, radius, 0f, caster,
				useOverride: true, centerOverride: new Vector2(center.x, center.y), facingOverride: Vector2.right);

			Apply(effectId, caster, skillId, scanned, 0);
		}

		// 리졸브 사본에서 먼저 찾는다 — 모디파이어가 반영된 값을 써야 한다 (설계 11.1).
		// 연쇄(ChainEffectIDs)로 딸려온 효과는 스킬의 효과 목록에 없으므로 테이블로 폴백한다.
		static Table_SkillEffect.Row resolveRow(SkillEffect effectId, UnitBase caster, EDT.Skill skillId)
		{
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
			}

			return row;
		}

		// ── 반복 예약 진입점 (SkillContainer 의 예약 큐가 호출) ────────
		//
		// 효과 전체(Apply)를 다시 돌리지 않는 이유 — 탐색·연쇄(ChainEffectIDs)·연출까지 반복된다.
		// 반복해야 하는 것은 "때리기 / 회복하기 / 쏘기" 뿐이다.

		// 다단히트의 2타 이후. 타격마다 치명타를 개별 판정한다 (설계 10.6).
		public static void RunDamageHit(SkillEffect effectId, UnitBase caster, EDT.Skill skillId, List<UnitBase> targets)
		{
			if (caster == null || caster.IsDead == true || targets == null)
			{
				return;
			}

			Table_SkillEffect.Row row = resolveRow(effectId, caster, skillId);
			if (row == null)
			{
				return;
			}

			DamageParams p;
			if (SkillEffectParams.TryParseDamage(row, out p) == false)
			{
				return;
			}

			bool swingHit = false;
			Vector2 swingOrigin = Vector2.zero;
			float nearestSqr = float.MaxValue;

			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];

				// 이미 죽은 대상의 남은 타격은 버린다 — 사망 판정이 중복된다.
				if (target == null || target.IsDead == true || target == caster)
				{
					continue;
				}

				if (dealDamage(caster, target, skillId, row, p) == true)
				{
					swingHit = true;
					Vector2 hitCenter = target.HitCenter;
					float sqr = (hitCenter - caster.HitCenter).sqrMagnitude;
					if (sqr < nearestSqr)
					{
						nearestSqr = sqr;
						swingOrigin = hitCenter;
					}
				}
			}

			// 루프 뒤로 targets 를 더 쓰지 않으므로 여기서 바로 통지해도 안전하다.
			if (swingHit == true)
			{
				notifyNormalHit(caster, skillId, swingOrigin);
			}
		}

		// 지속 회복의 2틱 이후. 회복량은 매 틱 재계산한다(시전자 스탯이 변할 수 있다).
		public static void RunHealTick(SkillEffect effectId, UnitBase caster, EDT.Skill skillId, List<UnitBase> targets)
		{
			if (caster == null || caster.IsDead == true || targets == null)
			{
				return;
			}

			Table_SkillEffect.Row row = resolveRow(effectId, caster, skillId);
			if (row == null)
			{
				return;
			}

			HealParams p;
			if (SkillEffectParams.TryParseHeal(row, out p) == false)
			{
				return;
			}

			healOnce(caster, targets, p);
		}

		// 연속 발사의 2발 이후. shotIndex 는 부채꼴 각도를 유지하기 위한 회차다.
		public static void RunProjectileShot(SkillEffect effectId, UnitBase caster, EDT.Skill skillId, List<UnitBase> targets, int shotIndex)
		{
			if (caster == null || caster.IsDead == true)
			{
				return;
			}

			Table_SkillEffect.Row row = resolveRow(effectId, caster, skillId);
			if (row == null)
			{
				return;
			}

			ProjectileParams p;
			if (SkillEffectParams.TryParseProjectile(row, out p) == false)
			{
				return;
			}

			Table_Projectile.Row projectile = Table_Projectile.Get(p.RefID);
			if (projectile == null)
			{
				return;
			}

			UnitBase target = pickAliveTarget(targets);
			launchShot(caster, target, skillId, projectile, p, shotIndex);
		}

		// ── EffectOrigin 해석 (설계 2.6) ──────────────────────────────

		static List<UnitBase> resolveOrigin(SkillEffectOrigin origin, UnitBase caster, List<UnitBase> scanned, int depth)
		{
			List<UnitBase> buffer = _originBuffers[depth];
			buffer.Clear();

			switch (origin)
			{
				case SkillEffectOrigin.Caster:
					buffer.Add(caster);
					break;

				case SkillEffectOrigin.Owner:
					// 소환물이 쓰면 주인, 그 외에는 시전자 자신이다 (설계 2.6).
					buffer.Add((caster.Owner != null) ? caster.Owner : caster);
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

			// 스윙(타격 1회) 단위로 집계한다 — 광역으로 여러 명을 맞춰도 콤보는 1만 올라야 한다.
			// 콤보 통지는 targets 사용이 모두 끝난 뒤로 미룬다 (아래 재진입 주석 참조).
			int hitSwings = 0;
			Vector2 swingOrigin = Vector2.zero;
			float nearestSqr = float.MaxValue;

			bool swingHit = false;
			for (int i = 0; i < targets.Count; i++)
			{
				UnitBase target = targets[i];
				if (target == null || target.IsDead == true || target == caster)
				{
					continue;
				}

				if (dealDamage(caster, target, skillId, row, p) == true)
				{
					swingHit = true;
					// 부채꼴·원형 탐색은 정렬돼 있지 않아 최근접을 여기서 직접 고른다.
					Vector2 hitCenter = target.HitCenter;
					float sqr = (hitCenter - caster.HitCenter).sqrMagnitude;
					if (sqr < nearestSqr)
					{
						nearestSqr = sqr;
						swingOrigin = hitCenter;
					}
				}
			}

			bool anyHit = swingHit;
			if (swingHit == true)
			{
				hitSwings++;
			}

			// 다단히트는 ①②의 예외 — 의도된 반복이다 (설계 5.8).
			// HitInterval 은 고정값이며 공속의 영향을 받지 않는다.
			// 2타 이후는 예약 큐가 대상 스냅샷을 그대로 물고 반복한다.
			if (p.HitCount > 1 && p.HitInterval > 0f && caster.SkillContainer != null)
			{
				caster.SkillContainer.ScheduleRepeat(SkillContainer.PendingKind.DamageHit,
					p.HitInterval, p.HitInterval, p.HitCount - 1, 1, skillId, row.ID, targets);
			}
			else if (p.HitCount > 1)
			{
				// 간격이 0이면 예약할 이유가 없다 — 같은 프레임에 마저 때린다.
				for (int hit = 1; hit < p.HitCount; hit++)
				{
					bool extraHit = false;
					for (int i = 0; i < targets.Count; i++)
					{
						UnitBase target = targets[i];
						if (target == null || target.IsDead == true || target == caster)
						{
							continue;
						}

						if (dealDamage(caster, target, skillId, row, p) == true)
						{
							extraHit = true;
							Vector2 hitCenter = target.HitCenter;
							float sqr = (hitCenter - caster.HitCenter).sqrMagnitude;
							if (sqr < nearestSqr)
							{
								nearestSqr = sqr;
								swingOrigin = hitCenter;
							}
						}
					}

					anyHit |= extraHit;
					if (extraHit == true)
					{
						hitSwings++;
					}
				}
			}

			// 콤보 통지는 targets 를 다 쓴 뒤에 몰아서 한다.
			//
			// 통지는 TriggerOnCombo → SkillExecutor.Execute 로 이어지고, 콤보 스킬의 지연이 0이면
			// 그 자리에서 SkillEffectApplier.Apply(depth:0) 까지 동기로 내려간다. 그 안의 resolveOrigin 이
			// _originBuffers[0] 을 Clear 하는데 여기서 순회 중인 targets 가 바로 그 버퍼라,
			// 스윙 도중에 통지하면 뒤따르는 다단히트 루프와 ScheduleRepeat 이 빈 리스트를 보게 된다.
			for (int s = 0; s < hitSwings; s++)
			{
				notifyNormalHit(caster, skillId, swingOrigin);
			}

			return anyHit;
		}

		// 평타 1타가 누군가에게 실제로 데미지를 넣었을 때만 콤보를 센다 (회피·막기는 제외).
		// 평타 여부 판정은 SkillContainer 가 한다 — 여기서는 스윙 단위로 알리기만 한다.
		// origin 은 그 스윙에서 가장 가까웠던 피격 대상의 위치다 (좌표 고정형 콤보 스킬의 중심).
		static void notifyNormalHit(UnitBase caster, EDT.Skill skillId, Vector2 origin)
		{
			if (caster.SkillContainer == null)
			{
				return;
			}

			caster.SkillContainer.NotifyNormalAttackHit(skillId, origin);
		}

		static bool dealDamage(UnitBase caster, UnitBase target, EDT.Skill skillId, Table_SkillEffect.Row row, in DamageParams p)
		{
			DamageCalculator.Result result = DamageCalculator.Calculate(caster, target, p.ScaleStat, p.Ratio, p.FlatValue, row.OnHitTrigger);
			if (result.IsAvoided == true)
			{
				// 무효화는 TakeDamage 를 타지 않아 DamageTakenEvent 가 나가지 않는다 — 여기서 따로 알린다.
				EventManager.Instance.Publish(new DamageAvoidedEvent(target, caster, result.IsBlocked));
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
				// 발동될 스킬이 좌표 고정형이면 이 대상 자리에서 터진다.
				Vector2 hitOrigin = target.HitCenter;
				caster.SkillContainer.TriggerOnHit(hitOrigin);
				if (result.IsCritical == true)
				{
					caster.SkillContainer.TriggerOnCrit(hitOrigin);
				}
			}

			return true;
		}

		static bool applyHeal(Table_SkillEffect.Row row, UnitBase caster, EDT.Skill skillId, List<UnitBase> targets)
		{
			HealParams p;
			if (SkillEffectParams.TryParseHeal(row, out p) == false || targets.Count == 0)
			{
				return false;
			}

			if (healOnce(caster, targets, p) == false)
			{
				return false;
			}

			// 지속 회복 — 2틱 이후는 예약 큐가 반복한다. 회복량은 틱마다 다시 계산된다.
			if (p.TickCount > 1 && p.TickInterval > 0f && caster.SkillContainer != null)
			{
				caster.SkillContainer.ScheduleRepeat(SkillContainer.PendingKind.HealTick,
					p.TickInterval, p.TickInterval, p.TickCount - 1, 1, skillId, row.ID, targets);
			}

			return true;
		}

		static bool healOnce(UnitBase caster, List<UnitBase> targets, in HealParams p)
		{
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

				float before = target.Vitals.Hp;
				target.Vitals.ModifyHp(amount);

				// 풀피 클램프로 실제 회복이 0이면 알리지 않는다 — 0 이 뜨는 팝업을 막는다.
				int healed = Mathf.RoundToInt(target.Vitals.Hp - before);
				if (healed > 0)
				{
					EventManager.Instance.Publish(new HealAppliedEvent(target, healed));
				}
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

		static bool applyStatChange(Table_SkillEffect.Row row, UnitBase caster, List<UnitBase> targets, BuffRuntime buffOwner)
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

				StatModifier mod = target.Stats.AddModifier(p.StatDetailID, p.Value, row.ID.ToString());
				anyApplied = true;

				// 버프가 부여한 효과면 회수를 버프에 맡긴다 — 만료·중첩 갱신이 한 곳에서만 일어나야
				// 버프는 남았는데 스탯만 빠지는 어긋남이 생기지 않는다.
				if (buffOwner != null)
				{
					buffOwner.RegisterModifier(mod);
					continue;
				}

				// Duration > 0 이면 시한부다. 회수를 시전자의 예약 큐에 걸어
				// 시전자가 죽거나 씬이 바뀌면 예약도 함께 사라지게 한다.
				if (p.Duration > 0f && mod != null)
				{
					if (caster.SkillContainer != null)
					{
						caster.SkillContainer.ScheduleModifierRemoval(p.Duration, target, mod);
					}
					else
					{
						Debug.LogWarning($"[SkillEffectApplier] 한시 StatChange 회수 경로 없음 — 영구 적용됩니다. Effect:{row.ID}");
					}
				}
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

			UnitBase target = pickAliveTarget(targets);

			// Interval 이 0이면 Count 개를 같은 프레임에 부채꼴로 뿌린다.
			if (p.Interval <= 0f)
			{
				for (int i = 0; i < p.Count; i++)
				{
					launchShot(caster, target, skillId, projectile, p, i);
				}

				return true;
			}

			// Interval 이 있으면 한 발씩 순차 발사한다. 2발 이후는 예약 큐가 맡는다.
			launchShot(caster, target, skillId, projectile, p, 0);

			if (p.Count > 1 && caster.SkillContainer != null)
			{
				caster.SkillContainer.ScheduleRepeat(SkillContainer.PendingKind.ProjectileShot,
					p.Interval, p.Interval, p.Count - 1, 1, skillId, row.ID, targets);
			}

			return true;
		}

		// shotIndex 로 부채꼴 각도를 계산한다 — 순차 발사에서도 각도가 유지되어야 한다.
		static void launchShot(UnitBase caster, UnitBase target, EDT.Skill skillId,
			Table_Projectile.Row projectile, in ProjectileParams p, int shotIndex)
		{
			Vector2 origin = caster.HitCenter;
			Vector2 baseDir = resolveFacing(caster, target, origin);

			float half = (p.Count - 1) * 0.5f;
			float angle = (p.Angle != 0f) ? (shotIndex - half) * p.Angle : 0f;
			Vector2 dir = (angle != 0f) ? (Vector2)(Quaternion.Euler(0f, 0f, angle) * baseDir) : baseDir;

			ProjectileData data = default(ProjectileData);
			data.direction = new Vector3(dir.x, dir.y, 0f);
			data.startPos = new Vector3(origin.x, origin.y, caster.transform.position.z);
			data.caster = caster;
			data.skillId = skillId;
			data.hitEffect = projectile.HitEffectID_1;
			data.target = target;
			data.hitRadius = projectile.ExplodeRadius;
			data.speedRate = (p.SpeedRate > 0f) ? p.SpeedRate : 1f;
			data.pierce = projectile.Pierce;

			ProjectileManager.Instance.Launch(projectile.PrefabPath, data);
		}

		static UnitBase pickAliveTarget(List<UnitBase> targets)
		{
			if (targets == null)
			{
				return null;
			}

			for (int i = 0; i < targets.Count; i++)
			{
				if (targets[i] != null && targets[i].IsDead == false)
				{
					return targets[i];
				}
			}

			return null;
		}

		// 소환 (설계 7장). Radius 는 소환물이 쓸 스킬의 ScanRange=0 을 채우는 값으로도 상속된다 (설계 3.5).
		static bool applySummon(Table_SkillEffect.Row row, UnitBase caster, List<UnitBase> targets)
		{
			SummonParams p;
			if (SkillEffectParams.TryParseSummon(row, out p) == false)
			{
				return false;
			}

			if (p.RefID == EDT.Summon.None)
			{
				Debug.LogError($"[SkillEffectApplier] Summon 효과에 RefID 가 없습니다 — Effect:{row.ID}");
				return false;
			}

			if (Table_Summon.Get(p.RefID) == null)
			{
				Debug.LogError($"[SkillEffectApplier] Summon 행 없음 — Effect:{row.ID} RefID:{p.RefID}");
				return false;
			}

			// EffectOrigin 이 Target/Location 이면 그 자리에, Caster/Owner 면 시전자 자리에 놓인다.
			Vector3 center = caster.transform.position;
			if (targets.Count > 0 && targets[0] != null && targets[0] != caster)
			{
				center = targets[0].transform.position;
			}

			int count = (p.Count > 0) ? p.Count : 1;
			SummonManager.Instance.SpawnAsync(caster, p.RefID, count, p.Duration, p.Radius, center).Forget();

			// 프리팹 로드가 남아 있어도 "소환을 걸었다"는 성공이다 — ChainEffectIDs 가 이어져야 한다.
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
		static void playEffectPresentation(Table_SkillEffect.Row row, List<UnitBase> targets, UnitBase excluded, bool hasCenter, Vector2 center)
		{
			// 좌표 고정 효과는 대상이 아니라 고정 좌표에서 1회 터진다 —
			// 회피에 성공해(대상 0명) 데미지가 안 들어가도 장판이 터지는 것은 보여야 한다.
			if (hasCenter == true)
			{
				if (string.IsNullOrEmpty(row.EffectVFX) == false)
				{
					VFXManager.Instance.PlayOneShot(row.EffectVFX, new Vector3(center.x, center.y, 0f));
				}

				if (string.IsNullOrEmpty(row.EffectSFX) == false)
				{
					AudioManager.Instance.PlaySFX(row.EffectSFX);
				}

				return;
			}

			if (string.IsNullOrEmpty(row.EffectVFX) == false)
			{
				for (int i = 0; i < targets.Count; i++)
				{
					UnitBase target = targets[i];
					if (target == null || target == excluded)
					{
						continue;
					}

					Vector2 hitCenter = target.HitCenter;
					VFXManager.Instance.PlayOneShot(row.EffectVFX, new Vector3(hitCenter.x, hitCenter.y, target.transform.position.z));
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
