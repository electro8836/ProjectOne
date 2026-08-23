using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;
using ProjectOne.Utils;

namespace ProjectOne.Buff
{
	// 버프 한 개의 런타임 상태.
	//
	// 버프는 수치를 직접 갖지 않는다 — EffectID_01/02 를 참조하고, 지속시간·최대중첩은
	// 부여하는 SkillEffect 가 정한다 (설계 8.2·8.3).
	// TickInterval 이 0이면 지속형(스탯 변화), 0보다 크면 주기 발동형(DoT·HoT)이다.
	public sealed class BuffRuntime
	{
		// 행동 차단에 쓰는 키 — 같은 버프가 걸고 푼 것만 정확히 회수하기 위해 ID를 섞는다.
		readonly string _blockKey;

		public EDT.Buff Id { get; private set; }
		public bool IsDebuff { get; private set; }
		public UnitBase Owner { get; private set; }
		public UnitBase Source { get; private set; }
		public bool IsInfinite { get; private set; }
		public float RemainingDuration { get; private set; }
		public float TickInterval { get; private set; }
		public EDT.Skill SourceSkill { get; private set; }

		// 현재 중첩 수. StackPolicy 가 Independent 일 때만 2 이상이 된다.
		public int Stack { get; private set; }

		IntervalTimer _intervalTimer;
		readonly List<StatModifier> _modHandles = new List<StatModifier>(2);
		SkillEffect _effect01;
		SkillEffect _effect02;
		ActionBlockType[] _blockFlags;
		bool _expired;

		public bool IsExpired
		{
			get { return _expired; }
		}

		public BuffRuntime(EDT.Buff id, UnitBase owner, UnitBase source, float duration, EDT.Skill sourceSkill)
		{
			Id = id;
			Owner = owner;
			Source = source;
			SourceSkill = sourceSkill;
			IsInfinite = duration <= 0f;
			RemainingDuration = IsInfinite ? 0f : duration;
			Stack = 1;
			_blockKey = "Buff_" + id;

			Table_Buff.Row row = Table_Buff.Get(id);
			if (row == null)
			{
				Debug.LogError($"[BuffRuntime] Table_Buff.Get({id}) == null");
				_expired = true;
				return;
			}

			IsDebuff = row.IsDebuff;
			TickInterval = row.TickInterval;
			_effect01 = row.EffectID_01;
			_effect02 = row.EffectID_02;
			_blockFlags = row.BlockFlags;

			applyBlockFlags();

			// 지속형(TickInterval = 0)은 부착 시 1회 적용한다. 주기형은 Tick 이 발동시킨다.
			if (TickInterval <= 0f)
			{
				applyEffects();
			}
		}

		// 같은 버프가 다시 걸렸을 때 — 정책은 BuffContainer 가 결정하고 여기서는 값만 갱신한다.
		public void Refresh(float duration)
		{
			IsInfinite = duration <= 0f;
			RemainingDuration = IsInfinite ? 0f : duration;
			_intervalTimer.Reset();
			_expired = false;
		}

		// 남은 시간에 가산 (StackPolicy = Extend)
		public void Extend(float duration)
		{
			if (IsInfinite == true || duration <= 0f)
			{
				return;
			}

			RemainingDuration += duration;
			_expired = false;
		}

		public void AddStack(int max)
		{
			if (max > 0 && Stack >= max)
			{
				return;
			}

			Stack++;
		}

		// BuffConsume 이 스택을 차감한다. 0이 되면 만료 처리된다.
		public void ConsumeStack(int count)
		{
			Stack -= count;
			if (Stack <= 0)
			{
				Stack = 0;
				_expired = true;
			}
		}

		public void Tick(float dt)
		{
			if (_expired == true)
			{
				return;
			}

			// 주기 발동형 — DoT/HoT. 중첩은 각자 독립 인스턴스이므로 여기서 곱하지 않는다.
			if (TickInterval > 0f)
			{
				int n = _intervalTimer.Tick(dt, TickInterval);
				for (int i = 0; i < n; i++)
				{
					applyEffects();
				}
			}

			if (IsInfinite == false)
			{
				RemainingDuration -= dt;
				if (RemainingDuration <= 0f)
				{
					RemainingDuration = 0f;
					_expired = true;
				}
			}
		}

		// StatChange 효과가 부착한 모디파이어 핸들을 회수 목록에 등록한다.
		public void RegisterModifier(StatModifier mod)
		{
			if (mod == null)
			{
				return;
			}

			_modHandles.Add(mod);
		}

		// 버프 종료 — 차단 해제 + 부착된 StatModifier 전량 회수
		public void Dispose()
		{
			releaseBlockFlags();

			if (Owner != null && Owner.Stats != null)
			{
				for (int i = 0; i < _modHandles.Count; i++)
				{
					Owner.Stats.RemoveModifier(_modHandles[i]);
				}
			}

			_modHandles.Clear();
			_expired = true;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		void applyEffects()
		{
			if (Owner == null)
			{
				return;
			}

			// 버프 효과의 시전자는 버프를 건 주체(Source)이고 대상은 Owner 다.
			applyOne(_effect01);
			applyOne(_effect02);
		}

		readonly List<UnitBase> _selfTarget = new List<UnitBase>(1);
		void applyOne(SkillEffect effectId)
		{
			if (effectId == SkillEffect.None)
			{
				return;
			}

			_selfTarget.Clear();
			_selfTarget.Add(Owner);

			UnitBase caster = (Source != null) ? Source : Owner;
			SkillEffectApplier.Apply(effectId, caster, SourceSkill, _selfTarget, 0);
		}

		// BlockFlags 자체가 효과다 — 기절 계열은 EffectID 가 비어 있어도 된다 (설계 8.2).
		void applyBlockFlags()
		{
			if (_blockFlags == null || Owner == null)
			{
				return;
			}

			bool blocksCast = false;
			for (int i = 0; i < _blockFlags.Length; i++)
			{
				switch (_blockFlags[i])
				{
					case ActionBlockType.Move:
						Owner.BlockMove(_blockKey);
						break;
					case ActionBlockType.Turn:
						if (Owner.Mover != null)
						{
							Owner.Mover.SetFacingLocked(true);
						}

						break;
					case ActionBlockType.Attack:
					case ActionBlockType.Cast:
						Owner.BlockSkill(_blockKey);
						blocksCast |= _blockFlags[i] == ActionBlockType.Cast;
						break;
				}
			}

			// Cast 차단은 진행 중인 시전도 취소한다 (설계 2.2).
			if (blocksCast == true && Owner.SkillContainer != null)
			{
				Owner.SkillContainer.CancelCasting();
				Owner.SkillContainer.CancelBehavior();
				Owner.SkillContainer.CancelAction();
			}
		}

		void releaseBlockFlags()
		{
			if (_blockFlags == null || Owner == null)
			{
				return;
			}

			for (int i = 0; i < _blockFlags.Length; i++)
			{
				switch (_blockFlags[i])
				{
					case ActionBlockType.Move:
						Owner.UnblockMove(_blockKey);
						break;
					case ActionBlockType.Turn:
						if (Owner.Mover != null)
						{
							Owner.Mover.SetFacingLocked(false);
						}

						break;
					case ActionBlockType.Attack:
					case ActionBlockType.Cast:
						Owner.UnblockSkill(_blockKey);
						break;
				}
			}
		}
	}
}
