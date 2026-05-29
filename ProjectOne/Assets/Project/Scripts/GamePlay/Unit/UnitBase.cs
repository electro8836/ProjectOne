using UnityEngine;
using EDT;
using ProjectOne.Combat;
using ProjectOne.Event;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;
using ProjectOne.Buff;

namespace ProjectOne.Unit
{
	public enum UnitType
	{
		None,
		Hero,
		Monster
	}

	// 진영 — 스킬 타겟 판별(Enemy/Friendly)의 기준
	public enum Faction
	{
		None,
		Player,
		Enemy,
		Neutral
	}

	public abstract class UnitBase : MonoBehaviour
	{
		[SerializeField] Faction _faction = Faction.None;

		// 0은 "미할당" — UnitFactory.InjectIDs 호출 전 상태
		protected int _id;       // 인스턴스 유일 ID (UnitFactory 카운터에서 발급)
		protected int _tableId;  // 테이블 행 ID (CharacterID 또는 MonsterID)

		protected StatContainer _stats;
		protected Vitals _vitals;
		protected UnitMover _mover;
		protected UnitAnimator _animator;
		protected SkillContainer _skillContainer;
		protected BuffContainer  _buffContainer;

		public bool IsDead { get; protected set; }

		public int GetID()
		{
			return _id;
		}

		public int GetTableID()
		{
			return _tableId;
		}

		// 파생 클래스가 자기 타입을 선언 — 컴파일 타임 보장
		public abstract UnitType GetUnitType();

		// UnitFactory 가 스폰 직후 1회 주입
		public void InjectIDs(int instanceId, int tableId)
		{
			_id = instanceId;
			_tableId = tableId;
		}

		// 외부 노출 (UI 등에서 조회)
		public StatContainer Stats
		{
			get { return _stats; }
		}

		public Vitals Vitals
		{
			get { return _vitals; }
		}

		public Faction Faction
		{
			get { return _faction; }
		}

		// 외부 팩토리에서 런타임 진영 지정
		public void SetFaction(Faction f)
		{
			_faction = f;
		}

		public SkillContainer SkillContainer
		{
			get { return _skillContainer; }
		}

		public BuffContainer BuffContainer
		{
			get { return _buffContainer; }
		}

		protected virtual void Awake()
		{
			_mover    = GetComponent<UnitMover>();
			_animator = GetComponent<UnitAnimator>();
		}

		protected virtual void OnEnable()
		{
			UnitContainer.Instance.Register(this);
		}

		protected virtual void OnDisable()
		{
			UnitContainer.Instance.Unregister(this);
		}

		// 외부 팩토리에서 스폰 후 주입
		public void InjectStats(StatContainer stats)
		{
			_stats = stats;
		}

		public void InjectVitals(Vitals vitals)
		{
			_vitals = vitals;
		}

		public void InjectSkillContainer(SkillContainer sc)
		{
			_skillContainer = sc;
		}

		public void InjectBuffContainer(BuffContainer bc)
		{
			_buffContainer = bc;
		}

		// Mover 상태를 Animator로 푸시 (중재 지점) + 스킬/버프 틱
		protected virtual void LateUpdate()
		{
			if (IsDead == true)
			{
				return;
			}

			if (_animator && _mover)
			{
				_animator.SetMoving(_mover.IsMoving);
				_animator.SetFacing(_mover.Facing);
			}

			// 버프 먼저 틱(스탯 변경이 같은 프레임 스킬 계산에 반영되도록), 이후 스킬
			float dt = Time.deltaTime;
			if (_buffContainer != null)
			{
				_buffContainer.Tick(dt);
			}
			if (_skillContainer != null)
			{
				_skillContainer.Tick(dt);
			}
		}

		// 스탯 변경 직후 외부에서 호출 — 현재 스탯값을 Animator에 푸시
		// 자동 감지는 안 함 (호출자 책임): 스폰 직후, 장비/버프 변경 직후 등
		public void RefreshAnimationStats()
		{
			if (_animator == null || _stats == null)
			{
				return;
			}

			_animator.SetAttackSpeed(_stats.GetStat(StatTypes.AtkSpeed));
			_animator.SetMoveSpeed(_stats.GetStat(StatTypes.MoveSpeed));
		}

		// 평타 모션 재생 진입점 — 판정·쿨타임은 후속 단계
		protected virtual void PlayAttackMotion()
		{
			if (IsDead == true)
			{
				return;
			}

			if (_animator)
			{
				_animator.PlayAttack();
			}
		}

		// 스킬 모션 재생 진입점 — 판정·쿨타임은 후속 단계
		protected virtual void PlaySkillMotion()
		{
			if (IsDead == true)
			{
				return;
			}

			if (_animator)
			{
				_animator.PlaySkill();
			}
		}

		// 피격 공통 처리: 피격 애니 + 넉백 (HP 차감/Die 호출은 파생 클래스 책임)
		protected void HandleHit(in DamageInfo info)
		{
			if (IsDead == true)
			{
				return;
			}

			// 피격 알림 (HP 차감 직전) — 전투로그/디버그 등에서 구독
			EventManager.Instance.Publish(new DamageTakenEvent(this, info.Attacker, info.Damage, info.SkillID));

			if (_animator)
			{
				_animator.PlayHit();
			}

			if (info.KnockbackPower > 0f)
			{
				if (_mover)
				{
					_mover.AddImpulse(info.KnockbackDir * info.KnockbackPower);
				}
			}
		}

		protected virtual void Die()
		{
			if (IsDead == true)
			{
				return;
			}
			IsDead = true;

			if (_animator)
			{
				_animator.SetDead(true);
			}

			if (_mover)
			{
				_mover.SetMoveEnabled(false);
			}
		}
	}
}
