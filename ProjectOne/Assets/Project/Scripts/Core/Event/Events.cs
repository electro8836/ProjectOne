using ProjectOne.Unit;
using EDT;
using System;
using UnityEngine;

namespace ProjectOne.Event
{
		public struct ResourceChangeEvent
		{
				public readonly int ResourceType;
				public readonly int PreviousAmount;
				public readonly int CurrentAmount;
				public readonly int Delta;

				ResourceChangeEvent(int resourceType, int previousAmount, int currentAmount)
				{
						this.ResourceType = resourceType;
						this.PreviousAmount = previousAmount;
						this.CurrentAmount = currentAmount;
						this.Delta = CurrentAmount - PreviousAmount;
				}
		}
		
		public readonly struct MonsterKillEvent
		{
				public readonly int MonsterType;
				public readonly int MonsterID;

				MonsterKillEvent(int monsterType, int monsterID)
				{
						this.MonsterType = monsterType;
						this.MonsterID = monsterID;
				}
		}

		// 유닛 사망 글로벌 알림 (퀘스트/킬카운트/사망 알림 UI 등에서 구독).
		// InstanceID: UnitBase.GetID() — 인스턴스 유일 식별
		// TableID:    UnitBase.GetTableID() — CharacterID/MonsterID
		public readonly struct UnitDiedEvent
		{
				public readonly int InstanceID;
				public readonly int TableID;
				public readonly UnitType UnitType;

				public UnitDiedEvent(int instanceID, int tableID, UnitType unitType)
				{
						this.InstanceID = instanceID;
						this.TableID = tableID;
						this.UnitType = unitType;
				}
		}

		// 스킬 실행 시작 알림 (직접 시전 / OnHit 프록 / Casting 시작 포함). 전투로그/이펙트/디버그 등에서 구독.
		public readonly struct SkillCastEvent
		{
				public readonly UnitBase Caster;
				public readonly SkillInfo SkillId;

				public SkillCastEvent(UnitBase caster, SkillInfo skillId)
				{
						this.Caster = caster;
						this.SkillId = skillId;
				}
		}

		// 피격 알림 (HP 차감 직전, 모든 유닛 공통). 데미지 텍스트/전투로그/디버그 등에서 구독.
		public readonly struct DamageTakenEvent
		{
				public readonly UnitBase Target;
				public readonly UnitBase Attacker;
				public readonly int Damage;
				public readonly int SkillId;   // DamageInfo.SkillID (0=평타 등)

				public DamageTakenEvent(UnitBase target, UnitBase attacker, int damage, int skillId)
				{
						this.Target = target;
						this.Attacker = attacker;
						this.Damage = damage;
						this.SkillId = skillId;
				}
		}

		// 유닛 스폰 글로벌 알림 (HeroAspect 외 보조 채널 — UI/사운드/튜토리얼 등).
		public readonly struct UnitSpawnedEvent
		{
				public readonly UnitBase Unit;
				public readonly UnitType UnitType;
				public readonly int InstanceID;
				public readonly int TableID;

				public UnitSpawnedEvent(UnitBase unit, UnitType unitType, int instanceID, int tableID)
				{
						this.Unit = unit;
						this.UnitType = unitType;
						this.InstanceID = instanceID;
						this.TableID = tableID;
				}
		}

		// 게임 흐름 상태 전이 완료 알림 (GameFlow가 EnterAsync 성공 후 발행).
		// StateType: 새로 진입한 상태의 구체 타입 (예: typeof(BootState))
		public readonly struct GameStateChangedEvent
		{
				public readonly Type StateType;

				public GameStateChangedEvent(Type stateType)
				{
						this.StateType = stateType;
				}
		}
}