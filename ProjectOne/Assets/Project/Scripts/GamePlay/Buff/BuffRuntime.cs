using System;
using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Audio;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;
using ProjectOne.Utils;

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
		public SkillEffect Effect { get; private set; }     // 부착 시 1회 적용되는 효과 — 코드 버프(대시 공격)가 경로 데미지 소스로도 참조
		public SkillInfo SourceSkill { get; private set; }  // 이 버프를 발동시킨 스킬 — 코드 버프가 ScanType/ScanParam 등을 읽는 데 사용

		float _intervalAccum;
		readonly List<StatModifier> _modHandles = new List<StatModifier>(2);
		IBuffBehavior _behavior;   // 코드로 정의된 버프 동작 (없으면 null — 데이터 버프)
		ITickableBuff _tickable;   // 매 프레임 갱신이 필요한 코드 버프 (없으면 null)
		bool _expired;
		VFXHandle _rootVfx;        // 버프 지속 동안 owner 에 부착되는 루프성 VFX
		AudioSfxHandle _rootSfx;   // 버프 지속 동안 재생되는 루프성 SFX

		public bool IsExpired
		{
			get { return _expired; }
		}

		public BuffRuntime(BuffInfo id, UnitBase owner, UnitBase source, float duration, float intervalSec, SkillInfo sourceSkill = SkillInfo.None)
		{
			Id = id;
			Owner = owner;
			Source = source;
			SourceSkill = sourceSkill;
			IsInfinite = duration <= 0f;
			RemainingDuration = IsInfinite ? 0f : duration;
			IntervalSec = intervalSec;

			Table_BuffInfo.Row row = Table_BuffInfo.Get(id);
			if (row != null)
			{
				IsDebuff = row.IsDebuff;
				IntervalEffect = row.IntervalEffect;
				Effect = row.Effect;

				// VFX: 버프가 적용된 유닛에 부착해 지속 동안 따라다니게 함 — 앵커가 Center 면 충돌체 중심만큼 띄움
				if (string.IsNullOrEmpty(row.VFX) == false && owner != null)
				{
					Vector3 offset = (row.VFXAnchor == SkillAnchorType.Center) ? (Vector3)(owner.HitCenter - (Vector2)owner.transform.position) : Vector3.zero;
					_rootVfx = VFXManager.Instance.Attach(row.VFX, owner.transform, offset);
				}

				// SFX: 버프 지속 동안 루프 재생, Dispose 에서 정지
				if (string.IsNullOrEmpty(row.SFX) == false)
				{
					_rootSfx = AudioManager.Instance.PlayLoopSFX(row.SFX);
				}

				// Effect: 부착 시 1회 발동 — owner를 caster로 해서 Self 효과가 owner에 적용되도록 함
				if (row.Effect != SkillEffect.None)
				{
					SkillEffectApplier.ApplyOnBuff(row.Effect, owner, source, this);
				}
			}

			// 코드로 정의된 버프(버프 ID 와 동일한 이름의 클래스)가 있으면 활성화 — 없으면 데이터 동작만 수행
			Type behaviorType = Type.GetType(string.Format("ProjectOne.Buff.{0}", id.ToString()));
			if (behaviorType != null)
			{
				_behavior = Activator.CreateInstance(behaviorType, owner, source) as IBuffBehavior;
				if (_behavior != null)
				{
					_tickable = _behavior as ITickableBuff;
					if (_tickable != null)
					{
						_tickable.SetHost(this);
					}

					_behavior.OnActivate();
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

			// 코드 버프의 프레임 갱신 — 자체 종료 조건(거리 도달·막힘 등) 충족 시 즉시 만료
			if (_tickable != null && _tickable.Tick(dt) == true)
			{
				_expired = true;
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
			// 코드 버프 비활성화 — 행동 차단 등 해제
			if (_behavior != null)
			{
				_behavior.OnDeactivate();
				_behavior = null;
				_tickable = null;
			}

			// RootVFX 회수 — 버프 종료와 함께 사라짐
			if (_rootVfx != null)
			{
				VFXManager.Instance.Release(_rootVfx);
				_rootVfx = null;
			}

			// RootSFX 정지 — 버프 종료와 함께 멈춤
			if (_rootSfx != null)
			{
				AudioManager.Instance.StopLoopSFX(_rootSfx);
				_rootSfx = null;
			}

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
