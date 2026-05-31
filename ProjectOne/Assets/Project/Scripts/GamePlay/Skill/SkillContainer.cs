using System.Collections.Generic;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Skill
{
	// 지연 예약 항목의 디스패치 종류
	public enum PendingKind
	{
		PlayAndApply,  // 모션 재생 + MotionEffectTime 후 효과 (Casting 대기 종료 후)
		ApplyEffects   // 효과만 적용 (MotionEffectTime 대기 종료 후)
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

		readonly UnitBase _owner;
		readonly Dictionary<SkillInfo, SkillRuntime> _byId = new Dictionary<SkillInfo, SkillRuntime>();
		readonly List<SkillRuntime> _ordered = new List<SkillRuntime>(8);
		readonly List<SkillInfo> _idView = new List<SkillInfo>(8);
		readonly List<PendingEffect> _pending = new List<PendingEffect>(8);

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
			rt.StartCooldown();
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
			if (kind == PendingKind.PlayAndApply)
			{
				SkillExecutor.RunPlayAndApply(id, _owner);
			}
			else
			{
				SkillExecutor.RunApplyEffects(id, _owner);
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
