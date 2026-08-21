using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Combat;
using ProjectOne.Skill;
using ProjectOne.Utils;
using ProjectOne.Unit.Stats;

namespace ProjectOne.Unit
{
	// 소환물 (설계 7장).
	//
	// 장판·포탑·추종형 펫을 전부 이 하나로 표현한다. AIType 이 그 셋을 가른다.
	// 장판이 따로 없는 이유 — Stationary 소환물에 CastingType=Aura 스킬을 등록하면
	// SkillContainer.tickAuras 가 그대로 주기 실행을 맡는다.
	[RequireComponent(typeof(UnitMover), typeof(UnitAnimator))]
	public class SummonUnit : UnitBase, IDamageable, IPoolable
	{
		private UnitBase _owner;

		private EDT.Summon _summonId = EDT.Summon.None;

		private Table_Summon.Row _row;

		// 남은 수명. 0 이하이면 영구다.
		private float _lifeRemaining;

		private bool _hasLifetime;

		// 소환 시 받은 SkillEffect.Radius — ScanRange=0 인 스킬이 이 값을 상속한다 (설계 3.5).
		private float _spawnRadius;

		// 주인 스탯 버전 — 달라졌을 때만 상속을 다시 건다.
		private int _ownerStatVersion = -1;

		// 이 인스턴스 전용 리졸브 캐시. 전역 패스스루 캐시에 쓰면 테이블이 오염된다.
		private readonly Dictionary<EDT.Skill, ResolvedSkill> _resolveCache = new Dictionary<EDT.Skill, ResolvedSkill>();

		public override UnitType GetUnitType()
		{
			return UnitType.Summon;
		}

		public override UnitBase Owner
		{
			get { return _owner; }
		}

		public EDT.Summon SummonId
		{
			get { return _summonId; }
		}

		public Table_Summon.Row Row
		{
			get { return _row; }
		}

		// 풀 생성 시 1회 — 원형 정보는 스폰마다 바뀌지 않는다.
		public void SetSummonRow(EDT.Summon id, Table_Summon.Row row)
		{
			_summonId = id;
			_row = row;
		}

		// 스폰마다 주입 — 주인 / 수명 / 반경. 풀 재사용이므로 매번 덮어써야 한다.
		public void SetSpawnContext(UnitBase owner, float duration, float radius)
		{
			_owner = owner;
			_lifeRemaining = duration;
			_hasLifetime = duration > 0f;
			_spawnRadius = radius;

			// 반경 상속이 바뀌면 지난 스폰의 사본은 무효다.
			_resolveCache.Clear();
			_ownerStatVersion = -1;

			if (_row != null && _row.ScaleByRadius == true && radius > 0f)
			{
				transform.localScale = new Vector3(radius, radius, 1f);
			}
			else
			{
				transform.localScale = Vector3.one;
			}

			if (_brain != null)
			{
				_brain.Blackboard.SummonOwner = owner;
				_brain.Blackboard.ResetForSpawn((owner != null) ? owner.CachedPos : (Vector2)transform.position);
			}
		}

		// 소환물은 SkillResolver 를 쓰지 않는다 — 모디파이어 출처가 없고,
		// 주인의 옵션이 소환물 스킬을 오염시켜서도 안 된다 (설계 11.2).
		//
		// 사본을 만드는 이유는 단 하나, ScanRange=0 을 소환 반경으로 채우기 위해서다.
		// 패스스루는 테이블 원본을 직참조하는 전역 캐시라 여기에 쓰면 다른 유닛까지 오염된다.
		public override ResolvedSkill Resolve(EDT.Skill id)
		{
			ResolvedSkill cached;
			if (_resolveCache.TryGetValue(id, out cached) == true)
			{
				return cached;
			}

			ResolvedSkill passthrough = ResolvedSkill.CreatePassthrough(id);
			if (passthrough == null)
			{
				return null;
			}

			// 채울 것이 없으면 전역 캐시를 그대로 쓴다 — 할당이 생기지 않는다.
			if (passthrough.Row.ScanRange > 0f || _spawnRadius <= 0f)
			{
				_resolveCache[id] = passthrough;
				return passthrough;
			}

			ResolvedSkill copy = new ResolvedSkill();
			copy.Id = passthrough.Id;
			copy.Row = ResolvedSkill.CopySkill(passthrough.Row);
			copy.Row.ScanRange = _spawnRadius;
			for (int i = 0; i < passthrough.Effects.Count; i++)
			{
				copy.Effects.Add(passthrough.Effects[i]);
			}

			_resolveCache[id] = copy;
			return copy;
		}

		public override void ManualTick(float dt)
		{
			base.ManualTick(dt);

			if (IsDead == true)
			{
				return;
			}

			// 주인이 사라지면 DieWithOwner 규칙을 따른다 (설계 7.2).
			if (_owner == null || _owner.IsDead == true)
			{
				if (_row != null && _row.DieWithOwner == true)
				{
					Die();
					return;
				}
			}

			inheritOwnerStats();

			if (_hasLifetime == true)
			{
				_lifeRemaining -= dt;
				if (_lifeRemaining <= 0f)
				{
					Die();
				}
			}
		}

		// 주인 스탯의 비율을 실시간으로 따라간다 (설계 7.4).
		// 매 틱 SetBase 를 다시 거는 대신 주인의 Version 만 비교한다 —
		// 장비 교체 한 번에 모디파이어가 수십 개 붙으므로 이벤트로는 감당이 안 된다.
		private void inheritOwnerStats()
		{
			if (_row == null || _owner == null || _owner.Stats == null || _stats == null)
			{
				return;
			}

			int version = _owner.Stats.Version;
			if (version == _ownerStatVersion)
			{
				return;
			}

			_ownerStatVersion = version;

			applyInherit(_row.InheritStatType_1, _row.InheritStatRatio_1);
			applyInherit(_row.InheritStatType_2, _row.InheritStatRatio_2);

			// MaxHp 상속으로 최대치가 바뀌면 UnitBase.tickHpNotify 가 다음 틱에 비율을 맞춘다.
			// 여기서 또 부르면 이중 적용된다.
		}

		private void applyInherit(Stat statType, float ratio)
		{
			if (statType == Stat.None || ratio == 0f)
			{
				return;
			}

			StatDetail detail;
			if (StatCatalog.TryGetBaseDetail(statType, out detail) == false)
			{
				Debug.LogError($"[SummonUnit] {statType} 의 Base 레이어 StatDetail 이 없습니다 — Summon:{_summonId}");
				return;
			}

			_stats.SetBase(detail, _owner.Stats.GetStat(statType) * ratio);
		}

		public void TakeDamage(in DamageInfo info)
		{
			HandleHit(in info);
			if (_vitals != null)
			{
				_vitals.ModifyHp(-info.Damage);
				if (_vitals.IsHpZero)
				{
					Die();
				}
			}
		}

		public void OnActivate()
		{
		}

		public void OnDeactivate()
		{
			_owner = null;
			_resolveCache.Clear();
		}

		void IDamageable.TakeDamage(in DamageInfo info)
		{
			TakeDamage(in info);
		}
	}
}
