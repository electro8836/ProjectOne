using System;
using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Combat;
using ProjectOne.Utils;
using ProjectOne.Event;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;
using ProjectOne.Buff;
using ProjectOne.Aura;
using ProjectOne.Unit.AI;

namespace ProjectOne.Unit
{
	public enum UnitType
	{
		None,
		Hero,
		Monster
	}

	public enum Faction
	{
		None,
		Player,
		Enemy,
		Neutral
	}

	public abstract class UnitBase : MonoBehaviour
	{
		[SerializeField]
		private Faction _faction;

		protected int _id;

		protected int _tableId;

		protected StatContainer _stats;

		protected Vitals _vitals;

		protected UnitMover _mover;

		protected UnitAnimator _animator;

		protected SkillContainer _skillContainer;

		protected BuffContainer _buffContainer;

		protected AuraContainer _auraContainer;

		protected AiBrain _brain;

		protected CircleCollider2D _collider;

		public bool IsDead { get; protected set; }

		// 프레임 캐시 — UnitSimulator 가 프레임당 1회 갱신. 핫 루프(충돌/분리)는 이 필드만 읽어
		// transform.position / collider.radius 네이티브 브릿지 호출을 피한다.
		public Vector2 CachedPos { get; private set; }

		public float CachedRadius { get; private set; }

		// 분리(Separation) 벡터 — UnitSimulator 가 몬스터 전체를 배치 계산해 기록
		public Vector2 CachedSeparation { get; internal set; }

		public float Radius
		{
			get
			{
				if (!(_collider != null))
				{
					return 0f;
				}

				return _collider.radius;
			}
		}

		// 콜라이더 중심 오프셋 — 충돌 기준점은 transform.position 이 아니라 transform.position + offset.
		public Vector2 ColliderOffset => (_collider != null) ? _collider.offset : Vector2.zero;

		// 프레임 시작 위치/반경을 캐시. UnitSimulator 가 모든 유닛에 대해 프레임당 1회 호출.
		// CachedPos 는 콜라이더 중심(transform.position + offset) 기준 — 모든 충돌/분리/AI 가 동일 기준 사용.
		public void RefreshFrameCache()
		{
			CachedPos = (Vector2)transform.position + ColliderOffset;
			CachedRadius = Radius;
		}

		public Vector2 HitCenter => (Vector2)this.transform.position + ((_collider != null) ? _collider.offset : Vector2.zero);

		public StatContainer Stats => _stats;

		public Vitals Vitals => _vitals;

		public Faction Faction => _faction;

		public SkillContainer SkillContainer => _skillContainer;

		public BuffContainer BuffContainer => _buffContainer;

		public AuraContainer AuraContainer => _auraContainer;

		public UnitMover Mover => _mover;

		// 행동 차단 키 집합 — 키별로 켜고 끄므로 여러 CC 가 겹쳐도 안전 (스턴+이동불가 동시 적용 등)
		// 같은 키 중복 Add/Remove 가 무해 → 중복 적용·해제 누락에 강함
		private readonly HashSet<string> _moveBlockKeys = new HashSet<string>();

		private readonly HashSet<string> _skillBlockKeys = new HashSet<string>();

		// 1초 주기 자동 회복 타이머 (브레이크 게이지 / 스테미너 / HP)
		private IntervalTimer _secondTimer;

		private bool _wasKnockbackImmune;

		public bool IsMoveBlocked => _moveBlockKeys.Count > 0;

		public bool IsSkillBlocked => _skillBlockKeys.Count > 0;

		public bool IsKnockbackImmune
		{
			get
			{
				if (_stats == null || _vitals == null) { return false; }
				return _stats.GetStat(StatInfo.BreakGage) > 0f && !_vitals.IsBreak;
			}
		}

		public void BlockMove(string key)
		{
			_moveBlockKeys.Add(key);
		}

		public void UnblockMove(string key)
		{
			_moveBlockKeys.Remove(key);
		}

		public void BlockSkill(string key)
		{
			_skillBlockKeys.Add(key);
		}

		public void UnblockSkill(string key)
		{
			_skillBlockKeys.Remove(key);
		}

		public int GetID()
		{
			return _id;
		}

		public int GetTableID()
		{
			return _tableId;
		}

		public abstract UnitType GetUnitType();

		public void SetIDs(int id, int tableId)
		{
			_id = id;
			_tableId = tableId;
		}

		public void SetFaction(Faction f)
		{
			_faction = f;
		}

		protected virtual void Awake()
		{
			_mover = this.GetComponent<UnitMover>();
			_animator = this.GetComponent<UnitAnimator>();
			_collider = this.GetComponent<CircleCollider2D>();
		}

		protected virtual void OnEnable()
		{
			UnitContainer.Instance.Register(this);
		}

		protected virtual void OnDisable()
		{
			// 앱/씬 종료 시엔 UnitContainer 가 먼저 파괴됐을 수 있어 null 가드.
			if (UnitContainer.HasInstance)
			{
				UnitContainer.Instance.Unregister(this);
			}
		}

		public void SetStats(StatContainer stats)
		{
			_stats = stats;
		}

		public void SetVitals(Vitals vitals)
		{
			_vitals = vitals;
		}

		public void SetSkillContainer(SkillContainer sc)
		{
			_skillContainer = sc;
		}

		public void SetBuffContainer(BuffContainer bc)
		{
			_buffContainer = bc;
		}

		public void SetAuraContainer(AuraContainer ac)
		{
			_auraContainer = ac;
		}

		// AI 자동전투 두뇌 주입 — 미주입(null) 이면 자동전투 안 함 (플레이어 직접조작 등)
		public void SetBrain(AiBrain brain)
		{
			_brain = brain;
		}

		// UnitSimulator 가 프레임당 1회 호출 — 개별 MonoBehaviour.LateUpdate 콜백 오버헤드 제거.
		// (애니메이션/CC/Buff/Skill/AI/브레이크게이지 갱신)
		public virtual void ManualTick(float dt)
		{
			// 발밑 Y 기준 sortingOrder 갱신 — 개별 UnitAnimator.LateUpdate 콜백 N개를 중앙 일괄 구동으로 대체
			if (_animator != null)
			{
				_animator.UpdateSorting();
			}

			if (!IsDead)
			{
				if (_animator != null && _mover != null)
				{
					_animator.SetMoving(_mover.IsMoving);
					_animator.SetFacing(_mover.Facing);
				}

				// 이동 차단 상태를 mover 에 반영 (스턴/이동불가 등 CC)
				if (_mover != null)
				{
					_mover.SetMoveEnabled(IsMoveBlocked == false);
				}

				float deltaTime = dt;
				if (_buffContainer != null)
				{
					_buffContainer.Tick(deltaTime);
				}

				if (_auraContainer != null)
				{
					_auraContainer.Tick(deltaTime);
				}

				if (_skillContainer != null)
				{
					_skillContainer.Tick(deltaTime);
				}

				// AI 두뇌 갱신 — 타겟 탐색/이동/스킬 결정 (주입된 경우만)
				if (_brain != null)
				{
					_brain.Tick(deltaTime);
				}

				// 1초 주기 자동 회복 (브레이크 게이지 / 스테미너 / HP)
				if (_vitals != null && _stats != null)
				{
					if (_secondTimer.Tick(deltaTime, 1f) > 0)
					{
						regenTick();
					}

					// 넉백 면역 상태(브레이크 게이지 보유 중) 변화 시 외곽선 토글
					bool isImmune = IsKnockbackImmune;
					if (isImmune != _wasKnockbackImmune)
					{
						_wasKnockbackImmune = isImmune;
						_animator?.SetOutlineEnabled(isImmune);
					}
				}
			}
		}

		// 1초 주기 자동 회복 — 브레이크 게이지 / 스테미너 / HP (호출부에서 _stats·_vitals null 보장)
		private void regenTick()
		{
			float breakRecovery = _stats.GetStat(StatInfo.BreakRecovery);
			if (breakRecovery > 0f)
			{
				if (_vitals.IsBreak)
				{
					_vitals.TickBreakRecover(breakRecovery);
				}
				else
				{
					_vitals.ModifyBreakGage(breakRecovery);
				}
			}

			float staminaRegen = _stats.GetStat(StatInfo.StaminaRegen);
			if (staminaRegen > 0f)
			{
				_vitals.ModifyStamina(staminaRegen);
			}

			// 체력 회복 — 이미 풀피면 ModifyHp 내부 Clamp 로 무해
			float hpRecovery = _stats.GetStat(StatInfo.HpRecovery);
			if (hpRecovery > 0f)
			{
				_vitals.ModifyHp(hpRecovery);
			}
		}

		public void RefreshAnimationStats()
		{
			if (!(_animator == null) && _stats != null)
			{
				_animator.SetAttackSpeed(_stats.GetStat(StatInfo.AtkSpeed));
				_animator.SetMoveSpeed(_stats.GetStat(StatInfo.MoveSpeed));
			}
		}

		protected virtual void PlayAttackMotion()
		{
			if (!IsDead && _animator != null)
			{
				_animator.PlayAttack();
			}
		}

		protected virtual void PlaySkillMotion()
		{
			if (!IsDead && _animator != null)
			{
				_animator.PlaySkill();
			}
		}

		protected void HandleHit(in DamageInfo info)
		{
			if (!IsDead)
			{
				EventManager.Instance.Publish(new DamageTakenEvent(this, info.Attacker, info.Damage, info.SkillID, info.IsCritical, info.IsSuperCritical));

				// 피격 시 공격자가 자신의 StaminaSteal 만큼 스테미나 회복 (MaxStamina 0 이면 무해)
				if (info.Damage > 0 && info.Attacker != null && info.Attacker.Stats != null && info.Attacker.Vitals != null)
				{
					float steal = info.Attacker.Stats.GetStat(StatInfo.StaminaSteal);
					if (steal > 0f)
					{
						info.Attacker.Vitals.ModifyStamina(steal);
					}
				}

				if (_animator != null && !IsKnockbackImmune)
				{
					_animator.PlayHit();
				}

				if (info.KnockbackPower > 0f && _mover != null && !IsKnockbackImmune)
				{
					_mover.AddImpulse(info.KnockbackDir * info.KnockbackPower);
					// 넉백 발생 → 진행 중인 캐스팅 취소
					_skillContainer?.CancelCasting();
				}
			}
		}

		protected virtual void Die()
		{
			if (!IsDead)
			{
				IsDead = true;
				// 캐스팅 중 사망 시 시전 상태/이동·스킬 차단/캐스팅 모션(IsCasting)을 함께 정리
				if (_skillContainer != null)
				{
					_skillContainer.CancelCasting();
				}

				if (_animator != null)
				{
					_animator.PlayDead();
				}

				if (_mover != null)
				{
					_mover.SetMoveEnabled(enabled: false);
				}

				EventManager.Instance.Publish(new UnitDiedEvent(_id, _tableId, GetUnitType(), HitCenter));
			}
		}

		public virtual void OnSpawnReset(Vector3 pos)
		{
			this.transform.position = pos;
			IsDead = false;
			_wasKnockbackImmune = false;
			if (_vitals != null)
			{
				_vitals.InitHp();
				_vitals.InitBreakGage();
				_vitals.InitStamina();
			}

			if (_animator != null)
			{
				_animator.ResetDead();
				_animator.SetOutlineEnabled(false);
			}

			if (_mover != null)
			{
				_mover.SetMoveEnabled(enabled: true);
			}
		}
	}
}
