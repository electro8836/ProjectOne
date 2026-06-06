using System.Collections.Generic;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 지연 예약 항목의 디스패치 종류
	public enum PendingKind
	{
		CastAttackMotion,  // 캐스팅: 공격모션 + VFX + SFX 전환 (캐스팅 종료 MotionEffectTime 초 전)
		ApplyEffects       // 효과 적용 (캐스팅 종료 시점, 또는 비캐스팅 MotionEffectTime 대기 종료 후)
	}

	// 유닛이 보유한 스킬을 등록/해제/발동/조회 (POCO)
	// - UnitBase.LateUpdate 에서 Tick(dt) 위임 호출
	public sealed class SkillContainer
	{
		// 지연 발동 예약 — 코루틴 대신 Tick 에서 카운트다운 (동시 다수 스킬 부하/할당 회피)
		struct PendingEffect
		{
			public float remaining;
			public SkillInfo id;
			public PendingKind kind;
		}

		// 캐스팅 중 이동/스킬 차단에 쓰는 차단 키
		const string CastingKey = "Casting";

		readonly UnitBase _owner;
		readonly Dictionary<SkillInfo, SkillRuntime> _byId = new Dictionary<SkillInfo, SkillRuntime>();
		readonly List<SkillRuntime> _ordered = new List<SkillRuntime>(8);
		readonly List<SkillInfo> _idView = new List<SkillInfo>(8);
		readonly List<PendingEffect> _pending = new List<PendingEffect>(8);

		// 캐스팅(시전) 진행 중 여부 — 시전 시작 시 true, 발동/취소 시 false
		bool _isCasting;
		public bool IsCasting => _isCasting;

		// 현재 캐스팅 중인 스킬 ID — 인디케이터가 자기 항목 종료를 정확히 판단하도록 노출 (미캐스팅 시 None)
		SkillInfo _castingId = SkillInfo.None;
		public SkillInfo CastingSkillId => _castingId;

		public SkillContainer(UnitBase owner)
		{
			_owner = owner;
		}

		public UnitBase Owner
		{
			get { return _owner; }
		}

		// 보유 스킬 등록 (source 미지정 = 영구)
		public void Register(SkillInfo id)
		{
			Register(id, string.Empty);
		}

		// source 태그 부착 등록 — RemoveAllFromSource 로 일괄 해제 가능
		public void Register(SkillInfo id, string source)
		{
			if (id == SkillInfo.None || _byId.ContainsKey(id) == true)
			{
				return;
			}

			SkillRuntime rt = new SkillRuntime(id, source);
			_byId.Add(id, rt);
			_ordered.Add(rt);

			// Passive 는 등록 즉시 상시 적용 (모션/쿨타임 없음)
			if (rt.CastingType == SkillCastingTypes.Passive)
			{
				SkillExecutor.ApplyPassive(id, _owner);
			}
		}

		public void Unregister(SkillInfo id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return;
			}

			_byId.Remove(id);
			_ordered.Remove(rt);
		}

		// 특정 source 로 등록된 스킬 전체 해제
		readonly List<SkillInfo> _removeBuffer = new List<SkillInfo>(4);
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

		// 쿨타임/사망 체크 후 발동. 성공 시 true.
		public bool TryCast(SkillInfo id)
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

			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return false;
			}

			// Passive(상시 적용) / OnHit(적중 시 확률 발동) 은 직접 시전 대상 아님
			if (rt.CastingType == SkillCastingTypes.Passive || rt.CastingType == SkillCastingTypes.OnHit)
			{
				return false;
			}

			if (rt.CanCast() == false)
			{
				return false;
			}

			SkillExecutor.Execute(id, _owner);
			if (rt.IsBasicAttack && _owner.Stats != null)
			{
				float atkSpeed = _owner.Stats.GetStat(StatInfo.AtkSpeed);
				rt.StartCooldown(atkSpeed > 0f ? rt.Cooltime / atkSpeed : rt.Cooltime);
			}
			else
			{
				rt.StartCooldown();
			}
			return true;
		}

		// 보유 스킬 ID 목록 — 내부 캐시 리스트 재구성 후 반환
		public IReadOnlyList<SkillInfo> GetAll()
		{
			_idView.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				_idView.Add(_ordered[i].Id);
			}

			return _idView;
		}

		public bool IsOnCooldown(SkillInfo id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return false;
			}

			return rt.IsOnCooldown;
		}

		public float GetRemainingCooldown(SkillInfo id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return 0f;
			}

			return rt.RemainingCooltime;
		}

		// 고유(Special) 스킬 여부 — AI 자동전투 셀렉터에서 제외 판정용
		public bool IsSpecial(SkillInfo id)
		{
			SkillRuntime rt;
			if (_byId.TryGetValue(id, out rt) == false)
			{
				return false;
			}

			return rt.Source == "Special";
		}

		// 보유 스킬 중 기본 공격(IsBasicAttack) 스킬 — AI 탐지반경/폴백 공격용. 없으면 None.
		public SkillInfo GetBasicAttack()
		{
			for (int i = 0; i < _ordered.Count; i++)
			{
				Table_SkillInfo.Row row = Table_SkillInfo.Get(_ordered[i].Id);
				if (row != null && row.IsBasicAttack == true)
				{
					return _ordered[i].Id;
				}
			}

			return SkillInfo.None;
		}

		public void Tick(float dt)
		{
			for (int i = 0; i < _ordered.Count; i++)
			{
				_ordered[i].Tick(dt);
			}

			// 지연 예약 카운트다운 — 역순 순회 + 스왑 제거
			for (int i = _pending.Count - 1; i >= 0; i--)
			{
				PendingEffect p = _pending[i];
				p.remaining -= dt;
				if (p.remaining <= 0f)
				{
					int last = _pending.Count - 1;
					_pending[i] = _pending[last];
					_pending.RemoveAt(last);
					dispatch(p.id, p.kind);
				}
				else
				{
					_pending[i] = p;
				}
			}
		}

		// 넉백 등 경직 발생 시 호출 — 예약된 모든 지연 효과를 취소
		public void CancelPendingEffects()
		{
			_pending.Clear();
		}

		// 캐스팅 시작 — 시전 시간(castTime) 동안 이동/스킬을 차단.
		// 종료 motionLead 초 전에 공격모션, 종료 시점에 효과 적용을 예약한다.
		public void BeginCasting(float castTime, float motionLead, SkillInfo id)
		{
			_isCasting = true;
			_castingId = id;
			_owner.BlockMove(CastingKey);
			_owner.BlockSkill(CastingKey);

			// 캐스팅 모션 진입 — 공격모션 전환(CastAttackMotion) 전까지 IsCasting(Bool) 유지
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

			float lead = UnityEngine.Mathf.Clamp(motionLead, 0f, castTime);
			Schedule(castTime - lead, id, PendingKind.CastAttackMotion);
			Schedule(castTime, id, PendingKind.ApplyEffects);
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

		// 캐스팅 종료 — 이동/스킬 차단 해제
		void EndCasting()
		{
			if (_isCasting == false)
			{
				return;
			}

			_isCasting = false;
			_castingId = SkillInfo.None;
			_owner.UnblockMove(CastingKey);
			_owner.UnblockSkill(CastingKey);

			// 캐스팅 모션 해제 — 정상 종료/취소/사망 모두 이 경로를 거친다
			UnitAnimator animator = _owner.GetComponent<UnitAnimator>();
			if (animator != null)
			{
				animator.SetCasting(false);
			}

			// 조준 고정 해제
			if (_owner.Mover != null)
			{
				_owner.Mover.SetFacingLocked(false);
			}
		}

		// SkillExecutor 가 지연 발동을 예약 — delay <= 0 이면 즉시 디스패치
		public void Schedule(float delay, SkillInfo id, PendingKind kind)
		{
			if (delay <= 0f)
			{
				dispatch(id, kind);
				return;
			}

			_pending.Add(new PendingEffect { remaining = delay, id = id, kind = kind });
		}

		void dispatch(SkillInfo id, PendingKind kind)
		{
			if (kind == PendingKind.CastAttackMotion)
			{
				// 캐스팅 종료 MotionEffectTime 초 전 — 캐스팅모션 종료 후 공격모션 재생 (효과는 ApplyEffects 시점)
				UnitAnimator animator = _owner.GetComponent<UnitAnimator>();
				if (animator != null)
				{
					animator.SetCasting(false);
				}

				SkillExecutor.RunCastAttackMotion(id, _owner);
			}
			else
			{
				// 효과 적용 — 캐스팅이면 시작 시점 facing 이 잠긴 상태로 스캔한 뒤 캐스팅 종료(차단/facing/인디케이터 해제)
				SkillExecutor.RunApplyEffects(id, _owner);
				EndCasting();
			}
		}

		// IsOnHitTrigger 공격 적중 시 호출 — 보유한 OnHit 스킬마다 CastingParam% 확률로 발동 (스킬별 1회)
		public void TriggerOnHitSkills()
		{
			if (_owner == null || _owner.IsDead == true)
			{
				return;
			}

			for (int i = 0; i < _ordered.Count; i++)
			{
				SkillRuntime rt = _ordered[i];
				if (rt.CastingType != SkillCastingTypes.OnHit)
				{
					continue;
				}

				if (rt.CanCast() == false)
				{
					continue;
				}

				if (UnityEngine.Random.Range(0, 100) < rt.CastingParam)
				{
					SkillExecutor.Execute(rt.Id, _owner);
					rt.StartCooldown();
				}
			}
		}
	}
}
