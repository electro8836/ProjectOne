using ProjectOne.Unit;
using EDT;
using System;
using UnityEngine;

namespace ProjectOne.Event
{
		public struct ResourceChangeEvent
		{
				public readonly EDT.CurrencyInfo CurrencyType;
				public readonly int PreviousAmount;
				public readonly int CurrentAmount;
				public readonly int Delta;

				public ResourceChangeEvent(EDT.CurrencyInfo currencyType, int previousAmount, int currentAmount)
				{
						this.CurrencyType = currencyType;
						this.PreviousAmount = previousAmount;
						this.CurrentAmount = currentAmount;
						this.Delta = currentAmount - previousAmount;
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

		// OnHitTarget 프록이 피격자 위치에서 발동될 때 — 인디케이터를 해당 월드 위치/방향에 잠시 표시.
		public readonly struct SkillProcAtTargetEvent
		{
				public readonly UnitBase Caster;
				public readonly SkillInfo SkillId;
				public readonly Vector2 Position;   // 피격자 HitCenter
				public readonly Vector2 Facing;     // 캐스터→피격자 방향

				public SkillProcAtTargetEvent(UnitBase caster, SkillInfo skillId, Vector2 position, Vector2 facing)
				{
						this.Caster = caster;
						this.SkillId = skillId;
						this.Position = position;
						this.Facing = facing;
				}
		}

		// 피격 알림 (HP 차감 직전, 모든 유닛 공통). 데미지 텍스트/전투로그/디버그 등에서 구독.
		public readonly struct DamageTakenEvent
		{
				public readonly UnitBase Target;
				public readonly UnitBase Attacker;
				public readonly int Damage;
				public readonly int SkillId;   // DamageInfo.SkillID (0=평타 등)
				public readonly bool IsCritical;
				public readonly bool IsSuperCritical;

				public DamageTakenEvent(UnitBase target, UnitBase attacker, int damage, int skillId, bool isCritical, bool isSuperCritical)
				{
						this.Target = target;
						this.Attacker = attacker;
						this.Damage = damage;
						this.SkillId = skillId;
						this.IsCritical = isCritical;
						this.IsSuperCritical = isSuperCritical;
				}
		}

		// 가드브레이크 발동 알림. 피격 유닛 위로 브레이크 UI를 띄우는 등에서 구독.
		public readonly struct GuardBreakTriggeredEvent
		{
				public readonly UnitBase Victim;
				public readonly UnitBase Attacker;

				public GuardBreakTriggeredEvent(UnitBase victim, UnitBase attacker)
				{
						this.Victim = victim;
						this.Attacker = attacker;
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

		// 유저데이터 로드 완료 알림 (DataLoadState가 Account 주입 + 시작데이터 보정 후 발행).
		// DevTester 등 로드 후 후처리(개발 데이터 오버라이드)에서 구독.
		public readonly struct DataLoadedEvent
		{
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

		// 웨이브 상태 전이 알림 (웨이브 모드). 메인HUD의 스킵 버튼/웨이브 표시 등에서 구독.
		// IsWaiting=true: 다음 웨이브 대기 중(스킵 버튼 노출), WaitSeconds: 대기 총 시간(초)
		public readonly struct WaveStateChangedEvent
		{
				public readonly int CurrentWave;   // 1-based 현재 웨이브
				public readonly int TotalWaves;
				public readonly bool IsWaiting;
				public readonly float WaitSeconds;

				public WaveStateChangedEvent(int currentWave, int totalWaves, bool isWaiting, float waitSeconds)
				{
						this.CurrentWave = currentWave;
						this.TotalWaves = totalWaves;
						this.IsWaiting = isWaiting;
						this.WaitSeconds = waitSeconds;
				}
		}

		// 메인HUD 스킵 버튼 → DefenseStageMode 가 구독해 웨이브 대기를 즉시 종료한다(Defense 모드일 때만 유효).
		public readonly struct WaveSkipRequestedEvent
		{
		}

		// 인벤토리 변경 알림 (획득/합성/강화수치 변경). 인벤토리 UI 등에서 구독.
		public readonly struct InventoryChangeEvent
		{
				public readonly int ItemId;
				public readonly int Count;
				public readonly int EnhanceLevel;

				public InventoryChangeEvent(int itemId, int count, int enhanceLevel)
				{
						this.ItemId = itemId;
						this.Count = count;
						this.EnhanceLevel = enhanceLevel;
				}
		}

		// 장착 프리셋 슬롯 변경 알림 (ItemId=0 은 해제). 캐릭터별 장착이므로 CharacterId 포함. 장비 UI 등에서 구독.
		public readonly struct PresetChangeEvent
		{
				public readonly int CharacterId;
				public readonly EquipmentTypes Slot;
				public readonly int ItemId;

				public PresetChangeEvent(int characterId, EquipmentTypes slot, int itemId)
				{
						this.CharacterId = characterId;
						this.Slot = slot;
						this.ItemId = itemId;
				}
		}

		// 캐릭터 변경 알림 (획득/선택/등급 변경). 캐릭터 목록/선택 UI 등에서 구독.
		public readonly struct CharacterChangeEvent
		{
				public readonly int CharacterId;

				public CharacterChangeEvent(int characterId)
				{
						this.CharacterId = characterId;
				}
		}

		// 오버레이 스택이 비었을 때 알림 (UIManager가 마지막 오버레이를 닫은 직후 발행).
		// 로비 탭 그룹 등에서 구독해 탭 선택을 해제한다.
		public readonly struct OverlayClosedEvent
		{
		}
}