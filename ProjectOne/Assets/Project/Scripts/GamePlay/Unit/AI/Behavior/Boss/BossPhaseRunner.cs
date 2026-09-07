using System.Collections.Generic;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Monsters;
using ProjectOne.Skill;

namespace ProjectOne.Unit.AI
{
	// 보스의 페이즈 상태와 전환 시퀀스를 소유한다 (BossBehavior 가 컴포지션으로 들고 있다).
	//
	// 흐름
	//   평상시  — 현재 페이즈의 스킬세트를 BossBehavior 가 우선순위대로 쓴다.
	//   전환    — HP 비율이 다음 페이즈의 HpThreshold 이하로 내려가면 보스를 무적으로 만들고
	//             전멸기를 긴 캐스팅으로 시전하며 기믹 코어를 GimmickCount 개 뿌린다.
	//   파훼    — 그중 GimmickRequired 개를 채우면 캐스팅을 끊고 보스를 기절시킨다.
	//             (코어 1개는 히어로가 그 자리에 일정 시간 머물러야 채워진다 — BossGimmickCore)
	//   실패    — 전멸기가 그대로 발동한다.
	//   어느 쪽이든 전환이 끝나면 다음 페이즈로 넘어간다 — 진행이 막히지 않는다.
	public sealed class BossPhaseRunner : IBossGimmickListener
	{
		// 무적 버프 지속시간. 전멸기 캐스팅보다 넉넉히 길게 잡고 전환 종료 시 명시적으로 해제한다.
		// 시간에 기대지 않는 이유 — 파훼로 일찍 끝나는 경로가 정상 경로다.
		private const float InvincibleDuration = 60f;

		private static readonly List<EDT.Skill> _emptySkills = new List<EDT.Skill>();

		// 기믹 통지(OnGimmickActivated)는 인자를 받지 않으므로 소유자를 들고 있어야 한다.
		private UnitBase _owner;

		private IReadOnlyList<Table_BossMonsterPhase.Row> _phases;
		private bool _loaded;

		// 현재 페이즈 인덱스 (_phases 기준). 데이터가 없으면 -1.
		private int _phaseIndex = -1;

		// 전환을 유발한 페이즈 인덱스 — 전환이 끝나면 여기로 넘어간다.
		private int _pendingIndex = -1;

		private bool _inTransition;
		private int _activated;

		// 파훼에 필요한 개수. 소환 개수(GimmickCount)와 다르다 — 4개 뿌리고 2개만 채우면 되는 식이다.
		private int _needed;

		// 이번 전환에서 뿌린 코어 — 실패/사망 시 직접 회수한다(코어는 수명이 없다).
		private readonly List<BossGimmickCore> _cores = new List<BossGimmickCore>(8);

		// 파훼 보상 적용용 1인 버퍼 — 프레임당 할당을 피한다.
		private readonly List<UnitBase> _selfBuffer = new List<UnitBase>(1);

		// 현재 페이즈의 스킬세트. 데이터가 없으면 빈 목록.
		public IReadOnlyList<EDT.Skill> CurrentSkillSet { get; private set; }

		public bool HasPhases
		{
			get { return _phases != null && _phases.Count > 0; }
		}

		// 전환 중이면 true — 호출자는 이동·전투를 건너뛴다.
		public bool Tick(UnitBase self)
		{
			load(self);

			if (HasPhases == false)
			{
				return false;
			}

			// 전환 도중 사망 — 코어와 무적이 월드에 남지 않게 정리만 한다(페이즈는 넘기지 않는다).
			if (self.IsDead == true)
			{
				if (_inTransition == true)
				{
					cleanup(self);
					_inTransition = false;
					_pendingIndex = -1;
				}

				return false;
			}

			if (_inTransition == true)
			{
				tickTransition(self);
				return _inTransition;
			}

			tryBeginTransition(self);
			return _inTransition;
		}

		// 풀 재사용 대비 — behavior 인스턴스는 풀 생성 시 1회만 만들어지고 리스폰마다 재생성되지 않는다.
		public void ResetForSpawn(UnitBase self)
		{
			if (_inTransition == true)
			{
				cleanup(self);
			}

			_inTransition = false;
			_activated = 0;
			_needed = 0;
			_pendingIndex = -1;
			_phaseIndex = HasPhases ? 0 : -1;
			CurrentSkillSet = currentSet();
		}

		// ── 기믹 통지 ─────────────────────────────────────────────────

		public void OnGimmickActivated()
		{
			if (_inTransition == false || _owner == null)
			{
				return;
			}

			_activated++;
			if (_activated < _needed)
			{
				return;
			}

			// 파훼 성사 — 캐스팅을 끊어 전멸기를 무산시킨다.
			if (_owner.SkillContainer != null)
			{
				_owner.SkillContainer.CancelCasting();
			}

			endTransition(_owner, broken: true);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void load(UnitBase self)
		{
			if (_loaded == true)
			{
				return;
			}

			_loaded = true;
			_owner = self;
			_phases = MonsterCatalog.GetBossPhases(self.GetTableID());
			_phaseIndex = HasPhases ? 0 : -1;
			CurrentSkillSet = currentSet();
		}

		private IReadOnlyList<EDT.Skill> currentSet()
		{
			if (_phaseIndex < 0 || _phases == null || _phaseIndex >= _phases.Count)
			{
				return _emptySkills;
			}

			return MonsterCatalog.GetSkillSet(_phases[_phaseIndex].SkillSetGroupID);
		}

		// HP 비율이 다음 페이즈의 임계 이하로 내려갔으면 전환을 시작한다.
		private void tryBeginTransition(UnitBase self)
		{
			int next = _phaseIndex + 1;
			if (next >= _phases.Count)
			{
				return;
			}

			Table_BossMonsterPhase.Row phase = _phases[next];
			if (hpRatio(self) > phase.HpThreshold)
			{
				return;
			}

			// 관문이 없는 페이즈는 곧바로 넘어간다.
			if (phase.PhaseSkillID == EDT.Skill.None)
			{
				_phaseIndex = next;
				CurrentSkillSet = currentSet();
				return;
			}

			SkillContainer sc = self.SkillContainer;
			if (sc == null)
			{
				return;
			}

			// 모션 중이거나 차단 상태면 이번 틱은 넘기고 다음 틱에 다시 시도한다.
			// 무적은 시전이 실제로 시작된 뒤에 건다 — 실패한 틱에 무적만 남는 것을 막는다.
			if (sc.TryCast(phase.PhaseSkillID) == false)
			{
				return;
			}

			if (self.BuffContainer != null)
			{
				self.BuffContainer.Apply(EDT.Buff.BUFF_Invincible, InvincibleDuration, 1, self, phase.PhaseSkillID);
			}

			_inTransition = true;
			_pendingIndex = next;
			_activated = 0;

			// GimmickRequired 가 비어 있으면 소환한 것을 전부 채워야 하는 것으로 본다.
			_needed = (phase.GimmickRequired > 0) ? phase.GimmickRequired : phase.GimmickCount;
			_cores.Clear();

			if (phase.GimmickCount > 0 && DropManager.HasInstance == true)
			{
				DropManager.Instance.SpawnGimmicks(self.HitCenter, phase.GimmickRadius, phase.GimmickCount, this, _cores);
			}
		}

		// 캐스팅이 끝났으면(발동 또는 넉백 등으로 중단) 전환을 마친다.
		private void tickTransition(UnitBase self)
		{
			SkillContainer sc = self.SkillContainer;
			if (sc != null && sc.IsCasting == true)
			{
				return;
			}

			endTransition(self, broken: false);
		}

		private void endTransition(UnitBase self, bool broken)
		{
			// 순서가 중요하다 — cleanup 이 무적을 먼저 풀어야 파훼 보상(기절)이 들어간다.
			// 무적인 동안에는 SkillEffectApplier.resolveOrigin 이 대상에서 걸러내므로,
			// 이 둘을 뒤집으면 기절이 조용히 사라진다.
			cleanup(self);

			if (broken == true)
			{
				applyBreakReward(self);
			}

			if (_pendingIndex >= 0)
			{
				_phaseIndex = _pendingIndex;
				CurrentSkillSet = currentSet();
			}

			_inTransition = false;
			_pendingIndex = -1;
			_activated = 0;
			_needed = 0;
		}

		// 무적 해제 + 남은 코어 회수. 전환이 어떤 이유로 끝나든 반드시 지나간다.
		private void cleanup(UnitBase self)
		{
			if (self != null && self.BuffContainer != null)
			{
				self.BuffContainer.Remove(EDT.Buff.BUFF_Invincible);
			}

			for (int i = 0; i < _cores.Count; i++)
			{
				if (_cores[i] != null)
				{
					_cores[i].Recall();
				}
			}

			_cores.Clear();
		}

		// 파훼 보상 — 보스 자신에게 기절을 건다(연쇄로 받는 피해 증가까지 붙는다).
		// 보스는 상태이상 면역이지만 이 효과가 거는 BUFF_BreakStun 은 IgnoreImmune 이라 통한다.
		// 그 기절의 Cast 차단이 전멸기 캐스팅도 끊는다 (BuffRuntime.applyBlockFlags).
		private void applyBreakReward(UnitBase self)
		{
			_selfBuffer.Clear();
			_selfBuffer.Add(self);
			SkillEffectApplier.Apply(SkillEffect.SE_Boss_Break_Stun_Buff, self, EDT.Skill.None, _selfBuffer, 0);
			_selfBuffer.Clear();

			// 게이지도 함께 파훼된다 — 남아 있던 양과 무관하게 0에서 다시 차오른다.
			MonsterBreak breakComponent = self.GetComponent<MonsterBreak>();
			if (breakComponent != null)
			{
				breakComponent.NotifyForcedBreak();
			}
		}

		private static float hpRatio(UnitBase self)
		{
			if (self.Vitals == null || self.Stats == null)
			{
				return 1f;
			}

			float maxHp = self.Stats.GetStat(Stat.Stat_MaxHp);
			if (maxHp <= 0f)
			{
				return 1f;
			}

			return self.Vitals.Hp / maxHp;
		}
	}
}
