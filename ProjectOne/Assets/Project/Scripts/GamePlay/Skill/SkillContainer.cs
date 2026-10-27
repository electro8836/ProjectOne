using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;

namespace ProjectOne.Skill
{
	// 유닛이 보유한 스킬을 등록/해제/발동/조회 (POCO)
	// - UnitBase.ManualTick 에서 Tick(dt) 위임 호출
	//
	// 지연 모델이 신규 설계에서 바뀌었다. 구조는 "스킬 1개 = 지연 1개"였으나
	// 이제 효과마다 EffectTime 이 달라 예약 항목이 효과 단위다 (설계 4.2 · 5.1).
	public sealed class SkillContainer
	{
		// 예약 항목이 무엇을 하는가.
		//
		// 다단히트 · 지속 회복 · 투사체 연속 발사는 "N번을 간격 T로 반복"이라는 같은 문제다.
		// 효과 전체를 다시 돌리면 탐색과 연쇄(ChainEffectIDs)까지 반복되므로, 반복 대상만 따로 부른다.
		public enum PendingKind
		{
			Effect = 0,			// 효과 1개를 통째로 적용 (탐색 결과 스냅샷 대상)
			DamageHit,			// 다단히트의 2타 이후
			HealTick,			// 지속 회복의 2틱 이후
			ProjectileShot,		// 연속 발사의 2발 이후
			RemoveModifier		// 한시 StatChange 회수
		}

		// 지연 발동 예약 — 코루틴 대신 Tick 에서 카운트다운 (동시 다수 스킬 부하/할당 회피)
		struct PendingEffect
		{
			public PendingKind kind;
			public float remaining;
			public EDT.Skill skillId;
			public SkillEffect effectId;
			public List<UnitBase> targets;	// 예약 시점의 탐색 결과 스냅샷

			// 반복 예약 — 남은 횟수와 간격. repeat <= 1 이면 이번이 마지막이다.
			public int repeat;
			public float interval;

			// 반복 회차 (0부터). 연속 발사가 부채꼴 각도를 계산할 때 쓴다.
			public int index;

			// RemoveModifier 전용 — 되돌릴 핸들
			public StatModifier modifier;

			// 좌표 고정 효과 — 발동 시점에 이 좌표를 중심으로 다시 탐색한다 (EffectOrigin=Location)
			public Vector2 center;
			public bool hasCenter;

			// 발동 시점에 시전자 기준으로 범위를 다시 탐색한다 (캐스팅형)
			public bool rescan;
		}

		// 캐스팅 중 이동/스킬 차단에 쓰는 차단 키
		const string CastingKey = "Casting";

		readonly UnitBase _owner;
		readonly Dictionary<EDT.Skill, SkillRuntime> _byId = new Dictionary<EDT.Skill, SkillRuntime>();
		readonly List<SkillRuntime> _ordered = new List<SkillRuntime>(8);
		readonly List<EDT.Skill> _idView = new List<EDT.Skill>(8);
		readonly List<PendingEffect> _pending = new List<PendingEffect>(8);

		// 대상 스냅샷 재사용 풀 — 예약이 소비되면 반납한다(프레임당 할당 회피)
		readonly Stack<List<UnitBase>> _targetListPool = new Stack<List<UnitBase>>(8);

		// Aura 스킬의 다음 틱까지 남은 시간 (Skill → remaining)
		readonly Dictionary<EDT.Skill, float> _auraTimers = new Dictionary<EDT.Skill, float>();

		// OnLowHP 검사 주기 카운터
		float _lowHpCheckTimer;

		// 콤보 카운터 — 캐릭터당 1개. 평타가 적중했을 때 +1, 리셋 없음 (설계 2.2)
		int _comboCount;
		public int ComboCount => _comboCount;

		// 캐스팅(시전) 진행 중 여부
		bool _isCasting;
		public bool IsCasting => _isCasting;

		// 시전 완료까지 남은 시간 — 0 이 되는 순간 공격 모션을 낸다(효과 발동보다 앞선다)
		float _castMotionRemaining;

		// 남은 시전 시간 — 보스 전환 시퀀스가 파훼 제한시간으로 읽는다(제한시간 = 캐스팅 시간).
		public float CastRemaining => _castMotionRemaining;
		bool _castMotionPlayed;

		// 스킬 동작(모션) 진행 중 남은 시간 — 이 동안 조준이 고정되고 다른 스킬을 겹쳐 시전하지 못한다.
		// 이동 정지는 AI(몬스터 behavior)가 IsInAction 을 보고 처리한다 — 히어로 조작감은 건드리지 않는다.
		float _actionRemaining;
		public bool IsInAction => _actionRemaining > 0f;

		// 시전 시점의 공속 배율 — 모션은 CastingParam 뒤에 시작하므로 그때 다시 구하지 않고 보관한다.
		// 캐스팅형이라도 평타(SkillCategory=Normal)면 공속을 받으므로 1 로 고정하면 안 된다.
		float _castUseSpeed = 1f;

		// 현재 캐스팅 중인 스킬 ID — 인디케이터가 자기 항목 종료를 판단하도록 노출
		EDT.Skill _castingId = EDT.Skill.None;
		public EDT.Skill CastingSkillId => _castingId;

		// 현재 시전의 고정 좌표 — 좌표 고정형(Location) 효과가 있을 때만 세팅된다.
		// 인디케이터가 원을 그릴 위치를 여기서 읽는다.
		Vector2 _castCenter;
		bool _hasCastCenter;
		public bool HasCastCenter => _hasCastCenter;
		public Vector2 CastCenter => _castCenter;

		// 활성 코드 스킬(behavior) 슬롯 — 매 프레임 Tick 으로 진행, 종료/취소 시 비움
		ISkillBehavior _activeBehavior;
		public bool IsRunningBehavior => _activeBehavior != null;

		public SkillContainer(UnitBase owner)
		{
			_owner = owner;
		}

		public UnitBase Owner
		{
			get { return _owner; }
		}

		// ── 등록 / 해제 ───────────────────────────────────────────────

		public void Register(EDT.Skill id)
		{
			Register(id, string.Empty);
		}

		// source 태그 부착 등록 — RemoveAllFromSource 로 일괄 해제 가능
		public void Register(EDT.Skill id, string source)
		{
			if (id == EDT.Skill.None || _byId.ContainsKey(id) == true)
			{
				return;
			}

			SkillRuntime rt = new SkillRuntime(id, source);

			// 쿨타임·모션 길이도 모디파이어 대상이므로 리졸브 값으로 덮는다.
			ResolvedSkill resolved = _owner.Resolve(id);
			if (resolved != null && resolved.IsValid == true)
			{
				rt.ReadFrom(resolved.Row);
			}

			_byId.Add(id, rt);
			_ordered.Add(rt);

			// Passive 는 등록 즉시 1회 적용, Aura 는 주기 타이머를 건다 (설계 2.2)
			if (rt.CastingType == SkillCastingTypes.Passive)
			{
				SkillExecutor.ApplyPassive(id, _owner);
			}
			else if (rt.CastingType == SkillCastingTypes.Aura)
			{
				_auraTimers[id] = 0f;	// 등록 즉시 첫 틱
			}
		}

		public void Unregister(EDT.Skill id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return;
			}

			_byId.Remove(id);
			_ordered.Remove(rt);
			_auraTimers.Remove(id);
		}

		// 리졸브가 무효화된 뒤 등록된 스킬의 사이클 값만 다시 읽는다.
		// 런타임을 재생성하지 않는 이유 — 진행 중인 쿨타임이 날아가면 장비 교체가 쿨타임 리셋 수단이 된다.
		public void RefreshFromResolve()
		{
			for (int i = 0; i < _ordered.Count; i++)
			{
				ResolvedSkill resolved = _owner.Resolve(_ordered[i].Id);
				if (resolved != null && resolved.IsValid == true)
				{
					_ordered[i].ReadFrom(resolved.Row);
				}
			}
		}

		readonly List<EDT.Skill> _removeBuffer = new List<EDT.Skill>(4);
		public void RemoveAllFromSource(string source)
		{
			if (string.IsNullOrEmpty(source) == true)
			{
				return;
			}

			_removeBuffer.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				if (_ordered[i].Source == source)
				{
					_removeBuffer.Add(_ordered[i].Id);
				}
			}

			for (int i = 0; i < _removeBuffer.Count; i++)
			{
				Unregister(_removeBuffer[i]);
			}

			_removeBuffer.Clear();
		}

		// ── 시전 ──────────────────────────────────────────────────────

		// 쿨타임/차단 체크 후 발동. 성공 시 true.
		public bool TryCast(EDT.Skill id)
		{
			if (_owner == null || _owner.IsDead == true)
			{
				return false;
			}

			// 스킬 차단(스턴/침묵 등) 상태면 시전 불가
			if (_owner.IsSkillBlocked == true)
			{
				return false;
			}

			// 스킬 모션이 진행 중이면 다른 스킬을 겹쳐 시전하지 않는다 — 한 번에 하나씩
			if (IsInAction == true)
			{
				return false;
			}

			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return false;
			}

			if (IsDirectCastable(rt.CastingType) == false)
			{
				return false;
			}

			if (rt.CanCast() == false)
			{
				return false;
			}

			float useSpeed = rt.GetUseSpeed(getAtkSpeed());
			SkillExecutor.Execute(id, _owner, useSpeed);

			// Casting 은 BeginCasting 이 이동/스킬을 이미 잠갔고, 코드 스킬(behavior)은 스스로 이동을 제어한다
			if (_isCasting == false && _activeBehavior == null)
			{
				beginAction(rt.GetActionTime(useSpeed));
			}

			// 콤보는 여기서 세지 않는다 — 평타가 실제로 적중했을 때
			// SkillEffectApplier 가 NotifyNormalAttackHit 으로 알린다.

			rt.StartCooldown(useSpeed);
			return true;
		}

		// 직접 눌러 시전할 수 있는 방식인가 — 조건 발동형과 상시형은 제외한다.
		public static bool IsDirectCastable(SkillCastingTypes type)
		{
			switch (type)
			{
				case SkillCastingTypes.Instant:
				case SkillCastingTypes.Casting:
					return true;
				default:
					return false;
			}
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public IReadOnlyList<EDT.Skill> GetAll()
		{
			_idView.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				_idView.Add(_ordered[i].Id);
			}

			return _idView;
		}

		public bool IsOnCooldown(EDT.Skill id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return false;
			}

			return rt.IsOnCooldown;
		}

		public float GetRemainingCooldown(EDT.Skill id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return 0f;
			}

			return rt.RemainingCooldown;
		}

		public SkillRuntime GetRuntime(EDT.Skill id)
		{
			SkillRuntime rt;
			_byId.TryGetValue(id, out rt);
			return rt;
		}

		// 고유(Special) 스킬 여부 — AI 자동전투 셀렉터에서 제외 판정용
		public bool IsSpecial(EDT.Skill id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return false;
			}

			return rt.Source == "Special";
		}

		// 보유 스킬 중 평타 — 배열 순서가 아니라 SkillCategory 로 판정한다 (설계 3장·몬스터 3장)
		public EDT.Skill GetBasicAttack()
		{
			for (int i = 0; i < _ordered.Count; i++)
			{
				if (_ordered[i].IsNormal == true)
				{
					return _ordered[i].Id;
				}
			}

			return EDT.Skill.None;
		}

		// 자동 시전 대상 스킬들의 최소 사거리 — AI 정지 거리 기준. 없으면 -1.
		public float GetMinSkillRange()
		{
			float min = -1f;
			for (int i = 0; i < _ordered.Count; i++)
			{
				SkillRuntime rt = _ordered[i];
				if (IsDirectCastable(rt.CastingType) == false || rt.Source == "Special")
				{
					continue;
				}

				// ScanRange 는 모디파이어 대상이므로 리졸브 결과를 봐야 한다("사거리 +20%" 옵션).
				ResolvedSkill resolved = _owner.Resolve(rt.Id);
				if (resolved == null || resolved.IsValid == false || resolved.Row.ScanRange <= 0f)
				{
					continue;
				}

				if (min < 0f || resolved.Row.ScanRange < min)
				{
					min = resolved.Row.ScanRange;
				}
			}

			return min;
		}

		// 평타 사거리 — 몬스터 AI 의 정지 거리 기준이다.
		// 사거리에 든 스킬은 접근 도중에 이미 나가므로, 끝까지 붙는 기준은 평타뿐이다. 평타가 없으면 -1.
		public float GetBasicAttackRange()
		{
			return getResolvedRange(GetBasicAttack());
		}

		// ScanRange 는 모디파이어 대상이므로 리졸브 결과에서 읽는다("사거리 +20%" 옵션)
		float getResolvedRange(EDT.Skill id)
		{
			if (id == EDT.Skill.None)
			{
				return -1f;
			}

			ResolvedSkill resolved = _owner.Resolve(id);
			if (resolved == null || resolved.IsValid == false)
			{
				return -1f;
			}

			return resolved.Row.ScanRange;
		}

		// ── 갱신 ──────────────────────────────────────────────────────

		public void Tick(float dt)
		{
			// 스킬 동작 시간 소진 — 다 되면 조준 잠금을 푼다
			if (_actionRemaining > 0f)
			{
				_actionRemaining -= dt;
				if (_actionRemaining <= 0f)
				{
					endAction();
				}
			}

			// 활성 코드 스킬 진행 — 자체 종료 조건 충족 시 OnEnd 후 슬롯 비움
			if (_activeBehavior != null && _activeBehavior.Tick(dt) == true)
			{
				EndBehavior();
			}

			for (int i = 0; i < _ordered.Count; i++)
			{
				_ordered[i].Tick(dt);
			}

			tickAuras(dt);
			tickLowHp(dt);
			tickCastMotion(dt);
			tickPending(dt);
		}

		// Aura — Cooldown 을 무시하고 CastingParam(TickInterval) 마다 스킬 전체를 재실행한다 (설계 2.2)
		readonly List<EDT.Skill> _auraBuffer = new List<EDT.Skill>(4);
		void tickAuras(float dt)
		{
			if (_auraTimers.Count == 0 || _owner == null || _owner.IsDead == true)
			{
				return;
			}

			_auraBuffer.Clear();
			Dictionary<EDT.Skill, float>.Enumerator e = _auraTimers.GetEnumerator();
			while (e.MoveNext() == true)
			{
				_auraBuffer.Add(e.Current.Key);
			}

			for (int i = 0; i < _auraBuffer.Count; i++)
			{
				EDT.Skill id = _auraBuffer[i];
				SkillRuntime rt;
				if (_byId.TryGetValue(id, out rt) == false)
				{
					continue;
				}

				float remaining = _auraTimers[id] - dt;
				if (remaining > 0f)
				{
					_auraTimers[id] = remaining;
					continue;
				}

				// TickInterval 이 0이면 매 프레임 실행이 되어버린다. 데이터 오류로 보고 건너뛴다.
				float interval = rt.CastingParam;
				if (interval <= 0f)
				{
					Debug.LogError($"[SkillContainer] Aura 스킬의 TickInterval(CastingParam)이 0 이하 — EDT.Skill:{id}");
					_auraTimers[id] = 1f;
					continue;
				}

				_auraTimers[id] = interval;
				SkillExecutor.Execute(id, _owner, 1f);
			}
		}

		// OnLowHP — 체력 비율이 임계 이하일 때 발동. 검사 주기는 코드 상수다.
		void tickLowHp(float dt)
		{
			_lowHpCheckTimer -= dt;
			if (_lowHpCheckTimer > 0f)
			{
				return;
			}

			_lowHpCheckTimer = SkillConstants.LOWHP_CHECK_INTERVAL;

			if (_owner == null || _owner.IsDead == true || _owner.Stats == null || _owner.Vitals == null)
			{
				return;
			}

			float maxHp = _owner.Stats.GetStat(Stat.Stat_MaxHp);
			if (maxHp <= 0f)
			{
				return;
			}

			float ratio = _owner.Vitals.Hp / maxHp;
			for (int i = 0; i < _ordered.Count; i++)
			{
				SkillRuntime rt = _ordered[i];
				if (rt.CastingType != SkillCastingTypes.OnLowHP || rt.CanCast() == false)
				{
					continue;
				}

				if (ratio <= rt.CastingParam)
				{
					SkillExecutor.Execute(rt.Id, _owner, 1f);
					rt.StartCooldown(1f);
				}
			}
		}

		void tickPending(float dt)
		{
			// 역순 순회 + 스왑 제거
			for (int i = _pending.Count - 1; i >= 0; i--)
			{
				PendingEffect p = _pending[i];
				p.remaining -= dt;
				if (p.remaining > 0f)
				{
					_pending[i] = p;
					continue;
				}

				int last = _pending.Count - 1;
				_pending[i] = _pending[last];
				_pending.RemoveAt(last);

				// 반복이 남았으면 대상 스냅샷을 넘겨 다시 예약한다. 스냅샷은 마지막 발동에서만 반납된다.
				if (p.repeat > 1)
				{
					PendingEffect next = p;
					next.repeat = p.repeat - 1;
					next.index = p.index + 1;
					next.remaining = p.interval;
					_pending.Add(next);

					dispatchWithoutRelease(p);
					continue;
				}

				dispatch(p);
			}
		}

		// 넉백 등 경직 발생 시 호출 — 예약된 모든 지연 효과를 취소 (설계 4.6)
		public void CancelPendingEffects()
		{
			for (int i = 0; i < _pending.Count; i++)
			{
				releaseTargetList(_pending[i].targets);
			}

			_pending.Clear();
		}

		// ── 지연 예약 ─────────────────────────────────────────────────

		// SkillExecutor 가 효과 1개의 지연 발동을 예약한다. delay <= 0 이면 즉시 실행.
		// targets 는 호출자의 버퍼일 수 있으므로 내부에서 복사해 스냅샷으로 보관한다.
		public void ScheduleEffect(float delay, EDT.Skill skillId, SkillEffect effectId, List<UnitBase> targets)
		{
			scheduleEffectInternal(delay, skillId, effectId, targets, false, Vector2.zero, false);
		}

		// 재탐색 예약 — 대상 스냅샷을 싣지 않는다. 발동 시점에 시전자 기준으로 다시 탐색하므로
		// 캐스팅 도중 범위를 벗어난 적은 맞지 않고, 뒤늦게 들어온 적은 맞는다 (캐스팅형 전용).
		public void ScheduleRescanEffect(float delay, EDT.Skill skillId, SkillEffect effectId)
		{
			scheduleEffectInternal(delay, skillId, effectId, null, false, Vector2.zero, true);
		}

		// 좌표 고정 예약 — 대상 스냅샷 대신 좌표를 싣는다. 발동 시점에 그 좌표로 다시 탐색하므로
		// 시전 후 범위를 벗어난 적은 맞지 않고, 뒤늦게 들어온 적은 맞는다 (EffectOrigin=Location).
		public void ScheduleEffect(float delay, EDT.Skill skillId, SkillEffect effectId, List<UnitBase> targets, Vector2 center)
		{
			scheduleEffectInternal(delay, skillId, effectId, targets, true, center, false);
		}

		void scheduleEffectInternal(float delay, EDT.Skill skillId, SkillEffect effectId, List<UnitBase> targets, bool hasCenter, Vector2 center, bool rescan)
		{
			PendingEffect p = default(PendingEffect);
			p.kind = PendingKind.Effect;
			p.remaining = delay;
			p.skillId = skillId;
			p.effectId = effectId;
			p.targets = rentTargetList(targets);
			p.repeat = 1;
			p.center = center;
			p.hasCenter = hasCenter;
			p.rescan = rescan;

			if (delay <= 0f)
			{
				dispatch(p);
				return;
			}

			_pending.Add(p);
		}

		// 반복 예약 — count 회를 interval 간격으로. 첫 발동은 delay 뒤다.
		// 다단히트 2타 이후 / 지속 회복 2틱 이후 / 연속 발사 2발 이후가 이 경로를 쓴다.
		public void ScheduleRepeat(PendingKind kind, float delay, float interval, int count, int startIndex,
			EDT.Skill skillId, SkillEffect effectId, List<UnitBase> targets)
		{
			if (count <= 0)
			{
				return;
			}

			PendingEffect p = default(PendingEffect);
			p.kind = kind;
			p.remaining = delay;
			p.interval = interval;
			p.repeat = count;
			p.index = startIndex;
			p.skillId = skillId;
			p.effectId = effectId;
			p.targets = rentTargetList(targets);

			_pending.Add(p);
		}

		// 한시 StatChange 회수 예약 — 소유자가 죽거나 씬이 바뀌면 큐와 함께 정리된다.
		public void ScheduleModifierRemoval(float delay, UnitBase target, StatModifier modifier)
		{
			if (target == null || modifier == null || delay <= 0f)
			{
				return;
			}

			PendingEffect p = default(PendingEffect);
			p.kind = PendingKind.RemoveModifier;
			p.remaining = delay;
			p.repeat = 1;
			p.modifier = modifier;
			p.targets = rentTargetList(null);
			p.targets.Add(target);

			_pending.Add(p);
		}

		void dispatch(PendingEffect p)
		{
			dispatchWithoutRelease(p);
			releaseTargetList(p.targets);

			// 캐스팅 스킬은 마지막 효과가 나간 뒤 차단을 푼다 — 정상 종료라 모션도 여기서 나간다.
			if (_isCasting == true && p.skillId == _castingId && hasPendingFor(_castingId) == false)
			{
				EndCasting();
			}
		}

		// 반복 예약은 스냅샷을 다음 회차가 물려받으므로 여기서 반납하지 않는다.
		void dispatchWithoutRelease(PendingEffect p)
		{
			switch (p.kind)
			{
				case PendingKind.Effect:
					if (p.rescan == true)
					{
						SkillExecutor.RunEffectRescan(p.skillId, p.effectId, _owner);
					}
					else if (p.hasCenter == true)
					{
						SkillExecutor.RunEffectAt(p.skillId, p.effectId, _owner, p.center);
					}
					else
					{
						SkillExecutor.RunEffect(p.skillId, p.effectId, _owner, p.targets);
					}

					break;

				case PendingKind.DamageHit:
					SkillEffectApplier.RunDamageHit(p.effectId, _owner, p.skillId, p.targets);
					break;

				case PendingKind.HealTick:
					SkillEffectApplier.RunHealTick(p.effectId, _owner, p.skillId, p.targets);
					break;

				case PendingKind.ProjectileShot:
					SkillEffectApplier.RunProjectileShot(p.effectId, _owner, p.skillId, p.targets, p.index);
					break;

				case PendingKind.RemoveModifier:
					removeModifier(p);
					break;
			}
		}

		private static void removeModifier(PendingEffect p)
		{
			if (p.targets.Count == 0)
			{
				return;
			}

			UnitBase target = p.targets[0];
			if (target != null && target.Stats != null)
			{
				target.Stats.RemoveModifier(p.modifier);
			}
		}

		bool hasPendingFor(EDT.Skill id)
		{
			for (int i = 0; i < _pending.Count; i++)
			{
				if (_pending[i].skillId == id)
				{
					return true;
				}
			}

			return false;
		}

		List<UnitBase> rentTargetList(List<UnitBase> source)
		{
			List<UnitBase> list = (_targetListPool.Count > 0) ? _targetListPool.Pop() : new List<UnitBase>(8);
			list.Clear();
			if (source != null)
			{
				for (int i = 0; i < source.Count; i++)
				{
					list.Add(source[i]);
				}
			}

			return list;
		}

		void releaseTargetList(List<UnitBase> list)
		{
			if (list == null)
			{
				return;
			}

			list.Clear();
			_targetListPool.Push(list);
		}

		// ── 코드 스킬 (behavior) ──────────────────────────────────────

		public void BeginBehavior(EDT.Skill id, ISkillBehavior behavior)
		{
			if (behavior == null)
			{
				return;
			}

			// 진행 중이던 코드 스킬이 있으면 먼저 정리 (정상적으로는 차단으로 막히지만 안전망)
			if (_activeBehavior != null)
			{
				EndBehavior();
			}

			_activeBehavior = behavior;
			behavior.SetContext(_owner, id);
			behavior.OnStart();
		}

		public void CancelBehavior()
		{
			if (_activeBehavior == null)
			{
				return;
			}

			EndBehavior();
		}

		void EndBehavior()
		{
			if (_activeBehavior == null)
			{
				return;
			}

			ISkillBehavior behavior = _activeBehavior;
			_activeBehavior = null;
			behavior.OnEnd();
		}

		// ── 캐스팅 ────────────────────────────────────────────────────

		// 캐스팅 시작 — 시전 시간 동안 이동/스킬을 차단하고 조준을 고정한다.
		// 모션(AnimName)은 여기서 내지 않는다. castTime 이 지난 뒤 tickCastMotion 이 낸다 —
		// 시작에 내면 트리거가 캐스팅 내내 살아 있다가 취소 시에도 공격 모션이 나가 버린다.
		public void BeginCasting(float castTime, EDT.Skill id, float useSpeed)
		{
			_isCasting = true;
			_castingId = id;
			_castUseSpeed = useSpeed;
			_castMotionRemaining = castTime;
			_castMotionPlayed = false;
			_owner.BlockMove(CastingKey);
			_owner.BlockSkill(CastingKey);

			UnitAnimator animator = _owner.GetComponent<UnitAnimator>();
			if (animator != null)
			{
				animator.SetCasting(true);
			}

			// 캐스팅 시작 시점의 조준 방향으로 고정 — Line/Sector 가 타겟을 추적해 범위 이탈 후 명중하는 것 방지
			if (_owner.Mover != null)
			{
				_owner.Mover.SetFacingLocked(true);
			}
		}

		// SkillExecutor 가 좌표 고정형 효과의 중심을 확정한 뒤 호출한다 — 인디케이터가 이 좌표에 원을 그린다.
		public void SetCastCenter(Vector2 center)
		{
			_castCenter = center;
			_hasCastCenter = true;
		}

		// 넉백 등으로 캐스팅 취소 — 예약된 발동을 제거하고 차단 해제 (쿨타임은 그대로 소모)
		public void CancelCasting()
		{
			if (_isCasting == false)
			{
				return;
			}

			EndCasting();
			CancelPendingEffects();
		}

		// 모션은 여기서 내지 않는다 — tickCastMotion 이 시전 완료 시점에 이미 냈다.
		// 정상 종료(효과 발동 완료)와 취소가 하는 일이 같아졌으므로 구분 인자를 두지 않는다.
		void EndCasting()
		{
			if (_isCasting == false)
			{
				return;
			}

			_isCasting = false;
			_castingId = EDT.Skill.None;
			_hasCastCenter = false;
			_castMotionRemaining = 0f;
			_castMotionPlayed = false;
			_owner.UnblockMove(CastingKey);
			_owner.UnblockSkill(CastingKey);

			UnitAnimator animator = _owner.GetComponent<UnitAnimator>();
			if (animator != null)
			{
				animator.SetCasting(false);
			}

			// 모션이 아직 돌고 있으면 조준을 풀지 않는다 — 남은 잠금은 endAction 이 모션 종료 시각에 푼다.
			// 취소 경로는 CancelCasting 과 CancelAction 이 항상 짝으로 불리므로 잠금이 남지 않는다.
			if (_owner.Mover != null && IsInAction == false)
			{
				_owner.Mover.SetFacingLocked(false);
			}
		}

		// 시전을 마친 순간 공격 모션을 낸다 — 데미지는 이 모션의 EffectTime 지점에 들어간다.
		// 이동 차단과 인디케이터는 EndCasting 까지 유지되므로 여기서 풀지 않는다.
		void tickCastMotion(float dt)
		{
			if (_isCasting == false || _castMotionPlayed == true)
			{
				return;
			}

			_castMotionRemaining -= dt;
			if (_castMotionRemaining > 0f)
			{
				return;
			}

			playCastMotion();
		}

		// 시전을 마친 순간의 공격 모션 — 애니메이터는 이 트리거로 캐스팅 자세에서 빠져나온다.
		void playCastMotion()
		{
			// 리졸브에 실패해도 다시 시도하지 않는다 — 매 프레임 재진입 방지
			_castMotionPlayed = true;

			ResolvedSkill resolved = _owner.Resolve(_castingId);
			if (resolved == null || resolved.IsValid == false)
			{
				return;
			}

			UnitAnimator animator = _owner.GetComponent<UnitAnimator>();
			if (animator != null)
			{
				animator.PlayMotion(resolved.Row.AnimName);
				animator.SetCasting(false);
			}

			// 모션이 시작되는 지금부터 모션 길이만큼 액션 락을 건다.
			// 캐스팅의 BlockMove 는 마지막 효과가 나갈 때 풀리는데 그 시점이 모션 종료보다 이르다 —
			// 그 틈에 AI 가 추적을 재개하면 스킬 모션이 이동 모션으로 잘린다.
			// 모션을 끝까지 보장해야 하므로 쿨타임에 깎이는 GetActionTime 이 아니라 GetAnimPlayTime 을 쓴다.
			SkillRuntime rt = GetRuntime(_castingId);
			if (rt != null)
			{
				beginAction(rt.GetAnimPlayTime(_castUseSpeed));
			}
		}

		// ── 액션(모션) 잠금 ──────────────────────────────────────────

		// 모션이 도는 동안 조준을 고정한다 — Line/Sector 스킬이 시전 도중 타겟을 따라 휘는 것을 막는다.
		void beginAction(float actionTime)
		{
			if (actionTime <= 0f)
			{
				return;
			}

			_actionRemaining = actionTime;
			if (_owner.Mover != null)
			{
				_owner.Mover.SetFacingLocked(true);
			}
		}

		void endAction()
		{
			_actionRemaining = 0f;
			if (_owner != null && _owner.Mover != null)
			{
				_owner.Mover.SetFacingLocked(false);
			}
		}

		// 넉백/사망/스폰 리셋으로 모션이 끊길 때 — 조준 잠금이 영구히 남는 것을 막는다
		public void CancelAction()
		{
			if (IsInAction == false)
			{
				return;
			}

			endAction();
		}

		// ── 조건 발동 (트리거) ────────────────────────────────────────

		// OnHitTrigger=TRUE 인 효과가 적중했을 때 호출 — OnHit 스킬을 확률 발동한다.
		// origin 은 이 발동을 유발한 대상의 위치다 (좌표 고정형 효과의 중심).
		public void TriggerOnHit(Vector2 origin)
		{
			triggerByChance(SkillCastingTypes.OnHit, origin);
		}

		// 치명타 발생 시 (OnHitTrigger=TRUE 인 효과에서만)
		public void TriggerOnCrit(Vector2 origin)
		{
			triggerByChance(SkillCastingTypes.OnCrit, origin);
		}

		// 피격 시 — OnHitTrigger 와 무관하게 동작한다 (설계 2.2)
		public void TriggerOnDamaged(Vector2 origin)
		{
			triggerByChance(SkillCastingTypes.OnDamaged, origin);
		}

		// 적 처치 시 — 수단 무관. 광역으로 3명을 처치하면 3회 호출된다.
		public void TriggerOnKill(Vector2 origin)
		{
			triggerByChance(SkillCastingTypes.OnKill, origin);
		}

		// 평타가 실제로 적을 때렸을 때 콤보를 센다 — 휘두르기만 해서는 세지 않는다.
		// 광역으로 여러 명을 맞춰도 타격 1회는 1 카운트다 (호출자가 스윙 단위로 부른다).
		public void NotifyNormalAttackHit(EDT.Skill id, Vector2 origin)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false || rt.IsNormal == false)
			{
				return;
			}

			_comboCount++;
			TriggerOnCombo(origin);
		}

		// n번째 평타마다 발동. 카운터는 캐릭터당 1개를 공유하고 각 스킬이 count % n 으로 판정한다.
		void TriggerOnCombo(Vector2 origin)
		{
			if (_owner == null || _owner.IsDead == true)
			{
				return;
			}

			for (int i = 0; i < _ordered.Count; i++)
			{
				SkillRuntime rt = _ordered[i];
				if (rt.CastingType != SkillCastingTypes.OnCombo || rt.CanCast() == false)
				{
					continue;
				}

				int period = Mathf.RoundToInt(rt.CastingParam);
				if (period <= 0)
				{
					continue;
				}

				if (_comboCount % period == 0)
				{
					SkillExecutor.Execute(rt.Id, _owner, 1f, true, origin);
					rt.StartCooldown(1f);
				}
			}
		}

		void triggerByChance(SkillCastingTypes type, Vector2 origin)
		{
			if (_owner == null || _owner.IsDead == true)
			{
				return;
			}

			for (int i = 0; i < _ordered.Count; i++)
			{
				SkillRuntime rt = _ordered[i];
				if (rt.CastingType != type || rt.CanCast() == false)
				{
					continue;
				}

				// 모든 확률은 0~1 배율이다 (설계 3.3)
				if (Random.value >= rt.CastingParam)
				{
					continue;
				}

				SkillExecutor.Execute(rt.Id, _owner, 1f, true, origin);
				rt.StartCooldown(1f);
			}
		}

		// CooldownReduce 효과가 호출 — 대상이 쿨다운 중이 아니면 무시된다.
		public void ReduceCooldown(EDT.Skill id, float ratio, float flatValue)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return;
			}

			// 회복량 = (최대 쿨타임 × Ratio) + FlatValue (설계 5.10)
			rt.ReduceCooldown(rt.Cooldown * ratio + flatValue);
		}

		float getAtkSpeed()
		{
			if (_owner == null || _owner.Stats == null)
			{
				return 1f;
			}

			return _owner.Stats.GetStat(Stat.Stat_AtkSpeed);
		}
	}
}
