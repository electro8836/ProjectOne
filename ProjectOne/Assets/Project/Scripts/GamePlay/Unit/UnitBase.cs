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

		protected CircleCollider2D _collider;

		public bool IsDead { get; protected set; }

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

		// 브레이크 게이지 자동 회복 1초 주기 누적기
		private float _breakRecoveryAccum;

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
			UnitContainer.Instance.Unregister(this);
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

		protected virtual void LateUpdate()
		{
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

				float deltaTime = Time.deltaTime;
				if (_buffContainer != null)
				{
					_buffContainer.Tick(deltaTime);
				}

				if (_skillContainer != null)
				{
					_skillContainer.Tick(deltaTime);
				}

				// 브레이크 게이지 자동 회복 — 1초마다 BreakRecovery 스탯만큼 (ModifyBreakGage 가 최댓값 Clamp)
				if (_vitals != null && _stats != null)
				{
					_breakRecoveryAccum += deltaTime;
					if (_breakRecoveryAccum >= 1f)
					{
						_breakRecoveryAccum -= 1f;
						float rec = _stats.GetStat(StatInfo.BreakRecovery);
						if (rec > 0f)
						{
							_vitals.ModifyBreakGage(rec);
						}
					}
				}
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
				if (_animator != null)
				{
					_animator.PlayHit();
				}

				if (info.KnockbackPower > 0f && _mover != null)
				{
					_mover.AddImpulse(info.KnockbackDir * info.KnockbackPower);
				}
			}
		}

		protected virtual void Die()
		{
			if (!IsDead)
			{
				IsDead = true;
				if (_animator != null)
				{
					_animator.PlayDead();
				}

				if (_mover != null)
				{
					_mover.SetMoveEnabled(enabled: false);
				}

				EventManager.Instance.Publish(new UnitDiedEvent(_id, _tableId, GetUnitType()));
			}
		}

		public virtual void OnSpawnReset(Vector3 pos)
		{
			this.transform.position = pos;
			IsDead = false;
			if (_vitals != null)
			{
				_vitals.InitHp();
				_vitals.InitBreakGage();
			}

			if (_animator != null)
			{
				_animator.ResetDead();
			}

			if (_mover != null)
			{
				_mover.SetMoveEnabled(enabled: true);
			}
		}
	}
}
