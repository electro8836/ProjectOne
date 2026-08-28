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
using ProjectOne.Unit.AI;

namespace ProjectOne.Unit
{
	public enum UnitType
	{
		None,
		Hero,
		Monster,
		Summon
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

		protected AiBrain _brain;

		protected CircleCollider2D _collider;

		// 체력 또는 최대 체력이 바뀐 프레임에 1회 발행한다.
		// 다단히트·흡혈·DoT 스택이면 한 프레임에 수십 번 변하므로 Vitals 는 dirty 만 세우고
		// 여기서 모아서 통지한다. 사망 판정(IsHpZero)은 합치면 안 되므로 TakeDamage 가 즉시 한다.
		public event System.Action<UnitBase> HpChanged;

		// 최대 체력 변경 감지용. StatContainer 는 지연 계산이라 변경 시점을 알려주지 못하므로
		// 프로젝트 관용구인 Version 비교로 잡는다 (SummonUnit.inheritOwnerStats 와 동일).
		private int _lastStatVersion = int.MinValue;

		private float _lastMaxHp;

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

		public UnitMover Mover => _mover;

		// 행동 차단 키 집합 — 키별로 켜고 끄므로 여러 CC 가 겹쳐도 안전 (스턴+이동불가 동시 적용 등)
		// 같은 키 중복 Add/Remove 가 무해 → 중복 적용·해제 누락에 강함
		private readonly HashSet<string> _moveBlockKeys = new HashSet<string>();

		private readonly HashSet<string> _skillBlockKeys = new HashSet<string>();

		// 1초 주기 자동 회복 타이머 (HP)
		private IntervalTimer _secondTimer;

		// 최종 이동 속도 — 스탯이 곧 이동 속도다 (Flat).
		// 코드 기본값을 두지 않는다. 유닛별 속도는 CharacterStat / MonsterStat 의 MoveSpeed_Base 가 정한다.
		public float MoveSpeed
		{
			get
			{
				if (_stats == null)
				{
					return 0f;
				}

				return _stats.GetStat(Stat.Stat_MoveSpeed);
			}
		}

		public bool IsMoveBlocked => _moveBlockKeys.Count > 0;

		public bool IsSkillBlocked => _skillBlockKeys.Count > 0;

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

		// 유닛 레벨 — 스탯 성장의 기준이자 데미지 방어 상수 K 의 기준(공격자 레벨).
		// 스폰 시 확정되며 실시간으로 변하지 않는다.
		public int Level { get; private set; } = 1;

		public void SetIDs(int id, int tableId)
		{
			_id = id;
			_tableId = tableId;
		}

		public void SetLevel(int level)
		{
			Level = level > 0 ? level : 1;
		}

		// 몬스터 등급 — 데미지 계산 [4]의 보스 보너스 판정에 쓴다. 히어로는 None.
		public virtual EDT.MonsterType MonsterType
		{
			get { return EDT.MonsterType.None; }
		}

		// 근접/원거리 성향 — 데미지 계산 [4]에서 어느 보너스를 쓸지 결정한다 (스킬 설계 10.4).
		// 몬스터는 두 스탯을 쓰지 않으므로 None 이 기본이다. Hero 만 장착 무기의 마스터리 값을 돌려준다.
		public virtual EDT.WeaponRangeType RangeType
		{
			get { return EDT.WeaponRangeType.None; }
		}

		// 소환 주인 — 소환물만 값을 돌려준다. EffectOrigin.Owner 해석의 유일한 근거다 (설계 2.6).
		public virtual UnitBase Owner
		{
			get { return null; }
		}

		// 스킬 리졸브 — 모디파이어 출처가 없는 유닛은 테이블 원본을 그대로 본다 (설계 11.2).
		// 사본을 만들지 않으므로 스폰마다 할당이 생기지 않는다. Hero 만 SkillResolver 캐시를 경유한다.
		public virtual ResolvedSkill Resolve(EDT.Skill id)
		{
			return ResolvedSkill.CreatePassthrough(id);
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
			UnitManager.Instance.Register(this);
		}

		protected virtual void OnDisable()
		{
			// 앱/씬 종료 시엔 UnitManager 가 먼저 파괴됐을 수 있어 null 가드.
			if (UnitManager.HasInstance)
			{
				UnitManager.Instance.Unregister(this);
			}
		}

		public void SetStats(StatContainer stats)
		{
			_stats = stats;
			resetHpTracking();
		}

		public void SetVitals(Vitals vitals)
		{
			_vitals = vitals;
			resetHpTracking();
		}

		// 컨테이너가 통째로 교체되면(몬스터 레벨 변경 등) 이전 추적값은 의미가 없다.
		// 그대로 두면 새 최대치를 "변경"으로 오인해 엉뚱한 Rescale 이 걸린다.
		private void resetHpTracking()
		{
			_lastStatVersion = int.MinValue;
			_lastMaxHp = 0f;
		}

		public void SetSkillContainer(SkillContainer sc)
		{
			_skillContainer = sc;
		}

		public void SetBuffContainer(BuffContainer bc)
		{
			_buffContainer = bc;
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

			// 체력/최대체력 변경을 프레임당 1회로 모아 통지 (체력바·HUD·보스UI 가 구독한다)
			tickHpNotify();

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
				}
			}
		}

		// 최대 체력 변경을 감지해 현재 체력 비율을 맞추고, 체력 변경을 프레임당 1회 통지한다.
		//
		// 최대체력 모디파이어는 장비 교체 한 번에 여러 개가 붙었다 떨어지므로 변경마다 반응하면
		// 중간 상태가 새어 나간다. 프레임 경계에서 한 번만 보면 그 문제가 없다.
		private void tickHpNotify()
		{
			if (_stats == null || _vitals == null)
			{
				return;
			}

			bool maxHpChanged = false;

			// Version 이 그대로면 최대치도 그대로다 — Dictionary 조회를 건너뛴다.
			int statVersion = _stats.Version;
			if (statVersion != _lastStatVersion)
			{
				_lastStatVersion = statVersion;

				float maxHp = _stats.GetStat(Stat.Stat_MaxHp);
				if (maxHp != _lastMaxHp)
				{
					// 첫 관측이면 기준값이 없다 — 기록만 하고 비율 보정은 다음 변경부터.
					if (_lastMaxHp > 0f)
					{
						_vitals.RescaleToNewMaxHp(_lastMaxHp);
					}

					_lastMaxHp = maxHp;
					maxHpChanged = true;
				}
			}

			if (_vitals.IsHpDirty == false && maxHpChanged == false)
			{
				return;
			}

			_vitals.ClearHpDirty();

			if (HpChanged != null)
			{
				HpChanged(this);
			}
		}

		// 1초 주기 자연 재생 — Stat_HpRegen 만큼 회복 (호출부에서 _stats·_vitals null 보장)
		private void regenTick()
		{
			// 이미 풀피면 ModifyHp 내부 Clamp 로 무해
			float hpRegen = _stats.GetStat(Stat.Stat_HpRegen);
			if (hpRegen > 0f)
			{
				_vitals.ModifyHp(hpRegen);
			}
		}

		public void RefreshAnimationStats()
		{
			if (!(_animator == null) && _stats != null)
			{
				_animator.SetAttackSpeed(_stats.GetStat(Stat.Stat_AtkSpeed));

				// 걷기 애니는 "기본 이속 대비 현재 이속" 비율로 재생한다.
				// 유닛마다 기본 이속이 다르므로 절대 속도를 넘기면 배속이 유닛별로 어긋난다.
				float baseMove = _stats.GetLayer(StatDetail.StatDetail_MoveSpeed_Base);
				float ratio = (baseMove > 0f) ? MoveSpeed / baseMove : 1f;
				_animator.SetMoveSpeedRatio(ratio);
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
				EventManager.Instance.Publish(new DamageTakenEvent(this, info.Attacker, info.Damage, info.SkillID, info.IsCritical));

				// 비선공 몬스터는 맞아야 반응한다 (몬스터 설계 2장 AggroType.Neutral).
				if (_brain != null)
				{
					_brain.OnDamaged(info.Attacker);
				}

				if (info.KnockbackPower > 0f && _mover != null)
				{
					_mover.AddImpulse(info.KnockbackDir * info.KnockbackPower);
					// 넉백 발생 → 진행 중인 캐스팅/모션 취소
					_skillContainer?.CancelCasting();
					_skillContainer?.CancelAction();
				}
			}
		}

		// 경직/기절 진입 연출 — 일반 피격은 모션을 내지 않는다.
		//
		// 캐스팅 중에는 무시한다. 애니메이터가 시전 자세를 유지할 뿐 아니라,
		// 소비되지 않은 Hit 트리거가 남아 캐스팅 종료 직후 뒤늦게 터지기 때문이다.
		// 행동 차단 버프가 Cast 플래그를 가지면 BuffRuntime 이 먼저 캐스팅을 취소하므로
		// 그 경우에는 정상적으로 모션이 나간다.
		public void PlayStaggerMotion()
		{
			if (IsDead == true || _animator == null)
			{
				return;
			}

			if (_skillContainer != null && _skillContainer.IsCasting == true)
			{
				return;
			}

			_animator.PlayHit();
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
					_skillContainer.CancelBehavior();
					_skillContainer.CancelAction();

					// 예약된 지연 효과 폐기 — CancelCasting 은 캐스팅 중일 때만 이걸 부른다.
					// 평타처럼 Instant 로 예약된 타격이 남으면 풀 재사용으로 되살아난 뒤
					// 첫 틱에 옛 대상 스냅샷을 그대로 때린다.
					_skillContainer.CancelPendingEffects();
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
			if (_vitals != null)
			{
				_vitals.InitHp();
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

			// 풀 재사용 — 이전 생의 모션 잠금이 남아 조준이 굳는 것을 막는다
			if (_skillContainer != null)
			{
				_skillContainer.CancelAction();

				// Die() 를 거치지 않고 회수되는 경로(스테이지 정리 등)도 있으므로 여기서 한 번 더 비운다.
				_skillContainer.CancelPendingEffects();
			}
		}
	}
}
