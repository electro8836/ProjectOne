using System.Collections.Generic;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;

namespace ProjectOne.Buff
{
	// 버프 한 개의 런타임 상태
	// - 생성 시 Table_BuffInfo.Effect 를 1회 적용 (IncreaseAttribute 인 경우 StatModifier 핸들 회수)
	// - IntervalEffect 가 설정되어 있고 intervalSec > 0 이면 주기적으로 발동
	// - duration <= 0 은 무한 지속(수동 해제 전까지)
	public sealed class BuffRuntime
	{
		public BuffInfo Id { get; private set; }
		public bool IsDebuff { get; private set; }
		public UnitBase Owner { get; private set; }
		public UnitBase Source { get; private set; }
		public bool IsInfinite { get; private set; }
		public float RemainingDuration { get; private set; }
		public float IntervalSec { get; private set; }
		public SkillEffect IntervalEffect { get; private set; }

		float _intervalAccum;
		readonly List<StatModifier> _modHandles = new List<StatModifier>(2);
		bool _expired;

		public bool IsExpired
		{
			get { return _expired; }
		}

		public BuffRuntime(BuffInfo id, UnitBase owner, UnitBase source, float duration, float intervalSec)
		{
			Id = id;
			Owner = owner;
			Source = source;
			IsInfinite = duration <= 0f;
			RemainingDuration = IsInfinite ? 0f : duration;
			IntervalSec = intervalSec;

			Table_BuffInfo.Row row = Table_BuffInfo.Get(id);
			if (row != null)
			{
				IsDebuff = row.IsDebuff;
				IntervalEffect = row.IntervalEffect;

				// Effect: 부착 시 1회 발동 — owner를 caster로 해서 Self 효과가 owner에 적용되도록 함
				if (row.Effect != SkillEffect.None)
				{
					SkillEffectApplier.ApplyOnBuff(row.Effect, owner, source, this);
				}
			}
		}

		// duration/interval만 갱신 (재적용 — 스택은 아니고 새로고침)
		public void Refresh(float duration, float intervalSec)
		{
			IsInfinite = duration <= 0f;
			RemainingDuration = IsInfinite ? 0f : duration;
			IntervalSec = intervalSec;
			_intervalAccum = 0f;
			_expired = false;
		}

		public void Tick(float dt)
		{
			if (_expired == true)
			{
				return;
			}

			// 주기 효과
			if (IntervalEffect != SkillEffect.None && IntervalSec > 0f)
			{
				_intervalAccum += dt;
				while (_intervalAccum >= IntervalSec)
				{
					_intervalAccum -= IntervalSec;
					SkillEffectApplier.ApplyOnBuff(IntervalEffect, Owner, Source, this);
				}
			}

			// 지속시간
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

		// SkillEffectApplier 가 IncreaseAttribute/DecreaseAttribute 적용 시 핸들을 등록
		public void RegisterModifier(StatModifier mod)
		{
			if (mod == null)
			{
				return;
			}
			_modHandles.Add(mod);
		}

		// 버프 종료 — 부착 시 적용된 StatModifier 들을 전부 회수
		public void Dispose()
		{
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
	}
}
