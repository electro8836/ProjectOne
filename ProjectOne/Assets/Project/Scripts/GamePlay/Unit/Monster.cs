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
				_brain.Blackboard.ResetForSpawn(new Vector2(origin.x, origin.y));
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
