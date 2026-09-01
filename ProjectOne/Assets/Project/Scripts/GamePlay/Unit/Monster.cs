using EDT;
using UnityEngine;
using ProjectOne.Combat;
using ProjectOne.Event;
using ProjectOne.Utils;

namespace ProjectOne.Unit
{
	[RequireComponent(typeof(UnitMover), typeof(UnitAnimator))]
	public class Monster : UnitBase, IDamageable, IPoolable
	{
		// 스폰 원점 — 리쉬(복귀) 판정과 필드 개체 리젠의 기준이다 (몬스터 설계 5장 · 8장).
		// 풀에서 재사용되므로 스폰마다 다시 찍힌다.
		public Vector3 SpawnOrigin { get; private set; }

		// 몬스터 등급. 보스 피해 보너스 판정과 HP바·이름표 표시 규칙이 이 값을 본다 (설계 2장).
		public override MonsterType MonsterType
		{
			get { return _monsterType; }
		}

		private MonsterType _monsterType = MonsterType.None;

		// 지역 드랍 그룹 — MonsterSpawn.RewardGroupID (보상 설계 9장).
		// 같은 몬스터라도 어느 스폰 조합에서 나왔느냐에 따라 드랍이 달라지므로 스폰 시점에 주입한다.
		// 고유 드랍(Monster.RewardGroupID)은 원형이 소유하므로 여기 두지 않는다.
		public int SpawnRewardGroupId { get; private set; }

		// 브레이크 게이지 — 엘리트/보스만 가진다. MonsterPool 이 등급을 보고 붙여 준다.
		private MonsterBreak _break;

		public override UnitType GetUnitType()
		{
			return UnitType.Monster;
		}

		// 스폰 시 위치를 확정하고 AI 앵커를 그 자리에 고정한다.
		public void SetSpawnOrigin(Vector3 origin)
		{
			SpawnOrigin = origin;

			if (_brain != null)
			{
				_brain.ResetForSpawn(new Vector2(origin.x, origin.y));
			}
		}

		// 원형 정보 주입 — 풀 생성 시 1회. 레벨과 달리 스폰마다 바뀌지 않는다.
		public void SetMonsterType(MonsterType type)
		{
			_monsterType = type;
		}

		// 지역 드랍 그룹 주입 — 스폰마다 바뀐다(풀 재사용이므로 매번 덮어써야 한다).
		public void SetSpawnRewardGroup(int groupId)
		{
			SpawnRewardGroupId = groupId;
		}

		// 브레이크 컴포넌트 주입 — 풀 생성 시 1회. 일반 몬스터는 null 로 남는다.
		public void SetBreak(MonsterBreak component)
		{
			_break = component;
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

			// 브레이크 차감은 사망 판정 뒤다 — 죽은 몬스터가 기절 모션으로 넘어가면 안 된다.
			// 타격 1회당 1번 차감되므로 다단히트는 히트 수만큼 깎인다.
			if (_break != null && IsDead == false)
			{
				Table_Skill.Row skill = Table_Skill.Get((EDT.Skill)info.SkillID);
				if (skill != null && skill.BreakDamage > 0f)
				{
					_break.ApplyBreakDamage(skill.BreakDamage);
				}
			}
		}

		// 사망 중에서도 "처치"만 보상 대상이다 — 스테이지 정리(ClearAlive)는 풀 반환이라 여기를 타지 않는다.
		protected override void Die()
		{
			bool wasAlive = IsDead == false;
			base.Die();

			if (wasAlive == true)
			{
				EventManager.Instance.Publish(new MonsterKillEvent(GetTableID(), Level, HitCenter, SpawnRewardGroupId));
			}
		}

		public override void ManualTick(float dt)
		{
			base.ManualTick(dt);

			// 죽으면 게이지 회복도 멈춘다 — base 가 버프/스킬/AI 를 IsDead 로 거르는 것과 같은 규칙.
			if (IsDead == false && _break != null)
			{
				_break.Tick(dt);
			}
		}

		// 풀 재사용 — 이전 생의 브레이크 상태가 남으면 다음 스폰이 0 게이지로 시작한다.
		public override void OnSpawnReset(Vector3 pos)
		{
			base.OnSpawnReset(pos);

			if (_break != null)
			{
				_break.ResetForSpawn();
			}
		}

		public void OnActivate()
		{
		}

		public void OnDeactivate()
		{
		}

		void IDamageable.TakeDamage(in DamageInfo info)
		{
			TakeDamage(in info);
		}
	}
}
