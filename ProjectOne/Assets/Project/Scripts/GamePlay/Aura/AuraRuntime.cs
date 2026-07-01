using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;
using ProjectOne.Utils;

namespace ProjectOne.Aura
{
	// 오라 한 개의 런타임 상태
	// - AttachedVFV 를 시전자(owner)에 부착
	// - 범위(SkillInfo.ScanType/ScanParam)를 RESCAN_INTERVAL 마다 스캔 (결과는 로컬 복사 — static 버퍼 오염 방지)
	// - 상시 슬롯(Interval=0): 대상 유닛의 스탯을 오라가 직접 관리(진입 시 AddModifier, 이탈 시 RemoveModifier).
	//   경계 왕복 떨림을 막기 위해 이탈 감지 후 LINGER 동안 유지(재진입 시 리셋).
	// - 주기 슬롯(Interval>0): Interval 마다 SkillEffectApplier.Apply 로 범위 내 대상에 효과 적용(데미지 등).
	public sealed class AuraRuntime
	{
		// 범위 재스캔 주기 — 매 프레임 전체 유닛 O(N) 순회를 피한다.
		const float RESCAN_INTERVAL = 0.15f;
		// 경계 이탈 유예 — 이탈 감지 후에도 이 시간만큼 상시 효과를 유지(왕복 흡수).
		const float LINGER = 0.4f;

		// 상시 슬롯의 대상 유닛별 적용 상태 (foreach 금지 → Dictionary 대신 인덱스 순회 List 로 관리)
		sealed class AppliedEntry
		{
			public UnitBase Unit;
			public StatModifier Mod;
			public float LingerRemain;
			public bool Seen;
		}

		// 상시(Interval=0) 효과 슬롯 — 스탯 modifier 를 오라가 직접 관리
		sealed class PersistentSlot
		{
			public StatInfo Attr;
			public float Value;                 // 부호 포함(Decrease 는 음수)
			public SkillApplyTarget ApplyTarget;
			public readonly List<AppliedEntry> Entries = new List<AppliedEntry>(8);
		}

		// 주기(Interval>0) 효과 슬롯 — Interval 마다 SkillEffectApplier 로 적용
		sealed class PeriodicSlot
		{
			public SkillEffect Effect;
			public float Interval;
			public IntervalTimer Timer;
		}

		public AuraInfo Id { get; private set; }
		public UnitBase Owner { get; private set; }
		public UnitBase Source { get; private set; }
		public bool IsInfinite { get; private set; }
		public float RemainingDuration { get; private set; }

		readonly SkillInfo _sourceSkill;
		SkillScanType _scanType;
		float _scanParam1;
		float _scanParam2;

		readonly List<PersistentSlot> _persistent = new List<PersistentSlot>(4);
		readonly List<PeriodicSlot> _periodic = new List<PeriodicSlot>(4);

		readonly List<UnitBase> _scannedLocal = new List<UnitBase>(32);
		IntervalTimer _rescanTimer;
		bool _firstTick = true;

		VFXHandle _rootVfx;
		bool _expired;

		public bool IsExpired
		{
			get { return _expired; }
		}

		public AuraRuntime(AuraInfo id, UnitBase owner, UnitBase source, float duration, SkillInfo sourceSkill)
		{
			Id = id;
			Owner = owner;
			Source = source;
			_sourceSkill = sourceSkill;
			IsInfinite = duration <= 0f;
			RemainingDuration = IsInfinite ? 0f : duration;

			// 범위 파라미터는 오라를 발동시킨 스킬에서 가져온다
			_scanType = SkillScanType.None;
			Table_SkillInfo.Row skillRow = Table_SkillInfo.Get(sourceSkill);
			if (skillRow != null)
			{
				_scanType = skillRow.ScanType;
				_scanParam1 = skillRow.ScanParam1;
				_scanParam2 = skillRow.ScanParam2;
			}
			else
			{
				Debug.LogError($"[AuraRuntime] 스캔 파라미터 소스 스킬 없음 — Aura:{id}, Skill:{sourceSkill}");
			}

			Table_AuraInfo.Row row = Table_AuraInfo.Get(id);
			if (row == null)
			{
				Debug.LogError($"[AuraRuntime] Aura 행 없음 — Aura:{id}");
				_expired = true;
				return;
			}

			// 오라 자체 VFX 를 시전자에 부착 (오라 종료 시 Dispose 에서 해제)
			if (string.IsNullOrEmpty(row.AttachedVFV) == false && owner != null)
			{
				_rootVfx = VFXManager.Instance.Attach(row.AttachedVFV, owner.transform);
			}

			SkillEffect[] effects = { row.Effect_1, row.Effect_2, row.Effect_3, row.Effect_4 };
			float[] intervals = { row.Effect1_Interval, row.Effect2_Interval, row.Effect3_Interval, row.Effect4_Interval };
			for (int i = 0; i < effects.Length; i++)
			{
				SetupSlot(effects[i], intervals[i]);
			}
		}

		// 슬롯 하나를 상시/주기로 분류해 등록
		void SetupSlot(SkillEffect effect, float interval)
		{
			if (effect == SkillEffect.None)
			{
				return;
			}

			if (interval > 0f)
			{
				PeriodicSlot slot = new PeriodicSlot();
				slot.Effect = effect;
				slot.Interval = interval;
				_periodic.Add(slot);
				return;
			}

			// 상시 슬롯: IncreaseAttribute/DecreaseAttribute 만 허용 — 그 외 타입이면 무시
			Table_SkillEffect.Row effRow = Table_SkillEffect.Get(effect);
			if (effRow == null)
			{
				Debug.LogError($"[AuraRuntime] 상시 슬롯 Effect 행 없음 — Aura:{Id}, Effect:{effect}");
				return;
			}

			float sign;
			if (effRow.EffectType == SkillEffectTypes.IncreaseAttribute)
			{
				sign = +1f;
			}
			else if (effRow.EffectType == SkillEffectTypes.DecreaseAttribute)
			{
				sign = -1f;
			}
			else
			{
				Debug.LogWarning($"[AuraRuntime] 상시 슬롯(Interval=0)은 Increase/DecreaseAttribute 만 지원 — Aura:{Id}, Effect:{effect}, Type:{effRow.EffectType} (무시)");
				return;
			}

			AttributeParams p;
			if (SkillEffectParams.TryParseAttribute(effRow, out p) == false)
			{
				return;
			}

			// 스탯 종류가 Modifier 불가(Base/Final)면 슬롯 비활성 (유닛 무관 규칙이므로 파싱 시 검증)
			if (Owner != null && Owner.Stats != null && Owner.Stats.CanAddModifier(p.AttrType) == false)
			{
				Debug.LogError($"[AuraRuntime] 상시 슬롯 Modifier 불가 스탯 — Aura:{Id}, Effect:{effect}, Stat:{p.AttrType}");
				return;
			}

			PersistentSlot persistent = new PersistentSlot();
			persistent.Attr = p.AttrType;
			persistent.Value = p.Value * sign;
			persistent.ApplyTarget = effRow.ApplyTarget;
			_persistent.Add(persistent);
		}

		public void Tick(float dt)
		{
			if (_expired == true)
			{
				return;
			}

			// 시전자 소멸/사망 시 오라 종료 (컨테이너가 다음 Tick 에 Dispose)
			if (Owner == null || Owner.IsDead == true)
			{
				_expired = true;
				return;
			}

			// 재스캔 게이팅 — 첫 틱은 강제 스캔(주기 발동 전 후보 확보)
			bool rescan = _firstTick || _rescanTimer.Tick(dt, RESCAN_INTERVAL) > 0;
			_firstTick = false;
			if (rescan == true)
			{
				Rescan();
			}

			// 주기 슬롯 — 자체 타이머로 발동, 발동 시 최근 스캔 캐시를 대상으로 적용
			for (int i = 0; i < _periodic.Count; i++)
			{
				PeriodicSlot slot = _periodic[i];
				int n = slot.Timer.Tick(dt, slot.Interval);
				for (int k = 0; k < n; k++)
				{
					SkillEffectApplier.Apply(slot.Effect, Owner, _sourceSkill, _scannedLocal);
				}
			}

			// 상시 슬롯 — 재스캔이 발생한 프레임에만 멤버십 갱신
			if (rescan == true)
			{
				for (int i = 0; i < _persistent.Count; i++)
				{
					UpdatePersistent(_persistent[i]);
				}
			}

			// 지속시간 감소
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

		// 범위 스캔 후 결과를 로컬 리스트로 복사 (static 공유 버퍼를 프레임 넘겨 보관하지 않는다)
		void Rescan()
		{
			_scannedLocal.Clear();
			if (_scanType == SkillScanType.None)
			{
				return;
			}

			List<UnitBase> scanned = TargetResolver.ScanByType(_scanType, _scanParam1, _scanParam2, Owner);
			for (int i = 0; i < scanned.Count; i++)
			{
				_scannedLocal.Add(scanned[i]);
			}
		}

		// 상시 슬롯 멤버십 diff — 진입 시 AddModifier, 이탈 후 LINGER 만료 시 RemoveModifier
		void UpdatePersistent(PersistentSlot slot)
		{
			// 1) 이번 스캔 관측 플래그 초기화
			for (int i = 0; i < slot.Entries.Count; i++)
			{
				slot.Entries[i].Seen = false;
			}

			// 2) 현재 멤버 순회 — 기존이면 유예 리셋, 신규면 modifier 부착
			List<UnitBase> members = TargetResolver.FilterByApplyTarget(_scannedLocal, slot.ApplyTarget, Owner);
			for (int i = 0; i < members.Count; i++)
			{
				UnitBase unit = members[i];
				if (unit == null || unit.IsDead == true || unit.Stats == null)
				{
					continue;
				}

				int idx = FindEntry(slot, unit);
				if (idx >= 0)
				{
					slot.Entries[idx].Seen = true;
					slot.Entries[idx].LingerRemain = LINGER;
					continue;
				}

				StatModifier mod = unit.Stats.AddModifier(slot.Attr, slot.Value);
				AppliedEntry entry = new AppliedEntry();
				entry.Unit = unit;
				entry.Mod = mod;
				entry.LingerRemain = LINGER;
				entry.Seen = true;
				slot.Entries.Add(entry);
			}

			// 3) 미관측 엔트리 — 유예 차감 후 만료/소멸이면 modifier 회수 (역순 제거)
			for (int i = slot.Entries.Count - 1; i >= 0; i--)
			{
				AppliedEntry entry = slot.Entries[i];
				if (entry.Seen == true)
				{
					continue;
				}

				bool gone = entry.Unit == null || entry.Unit.IsDead == true;
				if (gone == false)
				{
					entry.LingerRemain -= RESCAN_INTERVAL;
					if (entry.LingerRemain > 0f)
					{
						continue;
					}
				}

				if (entry.Unit != null && entry.Unit.Stats != null)
				{
					entry.Unit.Stats.RemoveModifier(entry.Mod);
				}

				slot.Entries.RemoveAt(i);
			}
		}

		static int FindEntry(PersistentSlot slot, UnitBase unit)
		{
			for (int i = 0; i < slot.Entries.Count; i++)
			{
				if (slot.Entries[i].Unit == unit)
				{
					return i;
				}
			}

			return -1;
		}

		// duration 갱신 (재적용 — 상시 modifier/주기 타이머는 유지)
		public void Refresh(float duration)
		{
			IsInfinite = duration <= 0f;
			RemainingDuration = IsInfinite ? 0f : duration;
			_expired = false;
		}

		// 오라 종료 — 부착된 모든 상시 modifier 회수 + VFX 해제
		public void Dispose()
		{
			for (int i = 0; i < _persistent.Count; i++)
			{
				PersistentSlot slot = _persistent[i];
				for (int k = 0; k < slot.Entries.Count; k++)
				{
					AppliedEntry entry = slot.Entries[k];
					if (entry.Unit != null && entry.Unit.Stats != null)
					{
						entry.Unit.Stats.RemoveModifier(entry.Mod);
					}
				}

				slot.Entries.Clear();
			}

			if (_rootVfx != null)
			{
				VFXManager.Instance.Release(_rootVfx);
				_rootVfx = null;
			}

			_expired = true;
		}
	}
}
