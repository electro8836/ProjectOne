using ProjectOne.Unit;
using EDT;
using System;
using UnityEngine;

namespace ProjectOne.Event
{
		public struct ResourceChangeEvent
		{
				public readonly EDT.Currency CurrencyType;
				public readonly int PreviousAmount;
				public readonly int CurrentAmount;
				public readonly int Delta;

				public ResourceChangeEvent(EDT.Currency currencyType, int previousAmount, int currentAmount)
				{
						this.CurrencyType = currencyType;
						this.PreviousAmount = previousAmount;
						this.CurrentAmount = currentAmount;
						this.Delta = currentAmount - previousAmount;
				}
		}
		
		// 몬스터 처치 알림 — 경험치·드랍 지급과 퀘스트 킬카운트가 구독한다.
		//
		// UnitDiedEvent 와 나누는 이유는 "처치"가 사망의 부분집합이기 때문이다.
		// 스테이지 전환 시 잔존 정리(ClearAlive)는 풀 반환이라 사망도 처치도 아니고,
		// 히어로 사망은 UnitDiedEvent 이지만 처치 보상 대상이 아니다.
		//
		// 킬 크레딧(가해자)을 싣지 않는다 — 싱글플레이 + 히어로 1명이라 판별할 것이 없다.
		public readonly struct MonsterKillEvent
		{
				public readonly int MonsterID;
				public readonly int Level;
				public readonly Vector2 Position;

				// 지역 드랍 그룹 (MonsterSpawn.RewardGroupID). 고유 드랍은 Monster 원형이 갖고 있어 싣지 않는다.
				public readonly int SpawnRewardGroupID;

				public MonsterKillEvent(int monsterID, int level, Vector2 position, int spawnRewardGroupID)
				{
						this.MonsterID = monsterID;
						this.Level = level;
						this.Position = position;
						this.SpawnRewardGroupID = spawnRewardGroupID;
				}
		}

		// 유닛 사망 글로벌 알림 (퀘스트/킬카운트/사망 알림 UI 등에서 구독).
		// InstanceID: UnitBase.GetID() — 인스턴스 유일 식별
		// TableID:    UnitBase.GetTableID() — CharacterID/MonsterID
		// Position:   사망 시점 HitCenter (드랍 생성 위치 등에서 사용)
		public readonly struct UnitDiedEvent
		{
				public readonly int InstanceID;
				public readonly int TableID;
				public readonly UnitType UnitType;
				public readonly Vector2 Position;

				public UnitDiedEvent(int instanceID, int tableID, UnitType unitType, Vector2 position)
				{
						this.InstanceID = instanceID;
						this.TableID = tableID;
						this.UnitType = unitType;
						this.Position = position;
				}
		}

		// 스킬 실행 시작 알림 (직접 시전 / OnHit 프록 / Casting 시작 포함). 전투로그/이펙트/디버그 등에서 구독.
		public readonly struct SkillCastEvent
		{
				public readonly UnitBase Caster;
				public readonly EDT.Skill SkillId;

				public SkillCastEvent(UnitBase caster, EDT.Skill skillId)
				{
						this.Caster = caster;
						this.SkillId = skillId;
				}
		}

		// OnHitTarget 프록이 피격자 위치에서 발동될 때 — 인디케이터를 해당 월드 위치/방향에 잠시 표시.
		public readonly struct SkillProcAtTargetEvent
		{
				public readonly UnitBase Caster;
				public readonly EDT.Skill SkillId;
				public readonly Vector2 Position;   // 피격자 HitCenter
				public readonly Vector2 Facing;     // 캐스터→피격자 방향

				public SkillProcAtTargetEvent(UnitBase caster, EDT.Skill skillId, Vector2 position, Vector2 facing)
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

				public DamageTakenEvent(UnitBase target, UnitBase attacker, int damage, int skillId, bool isCritical)
				{
						this.Target = target;
						this.Attacker = attacker;
						this.Damage = damage;
						this.SkillId = skillId;
						this.IsCritical = isCritical;
				}
		}

		// 공격 무효화 알림 (회피/막기 — HP 변화가 없다). 데미지 텍스트의 MISS/BLOCK 표시에서 구독.
		// DamageTakenEvent 로는 담을 수 없다 — SkillEffectApplier.dealDamage 가
		// TakeDamage 호출 전에 조기 리턴하므로 그 이벤트 자체가 나가지 않는다.
		public readonly struct DamageAvoidedEvent
		{
				public readonly UnitBase Target;
				public readonly UnitBase Attacker;
				public readonly bool IsBlocked;   // false=회피(MISS), true=막기(BLOCK)

				public DamageAvoidedEvent(UnitBase target, UnitBase attacker, bool isBlocked)
				{
						this.Target = target;
						this.Attacker = attacker;
						this.IsBlocked = isBlocked;
				}
		}

		// 회복 적용 알림 (최대체력 클램프 후 실제로 오른 양). 데미지 텍스트/전투로그 등에서 구독.
		// 풀피라 실제 회복이 0이면 발행하지 않는다 — 0 이 뜨는 팝업을 막는다.
		public readonly struct HealAppliedEvent
		{
				public readonly UnitBase Target;
				public readonly int Amount;

				public HealAppliedEvent(UnitBase target, int amount)
				{
						this.Target = target;
						this.Amount = amount;
				}
		}

		// 브레이크 발동 알림 (게이지 0 도달 또는 보스 패턴 파훼). 데미지 텍스트의 BREAK 표시에서 구독.
		// HP 변화가 없어 DamageTakenEvent 로는 담을 수 없다.
		public readonly struct MonsterBrokenEvent
		{
				public readonly UnitBase Target;

				public MonsterBrokenEvent(UnitBase target)
				{
						this.Target = target;
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
		// IsWaiting=true: 웨이브 클리어 배너("N/M단계 방어 완료"), WaitSeconds: 대기 총 시간(초)
		// CanSkip=true: 다음 웨이브가 있어 스킵 버튼 노출. 마지막 웨이브 클리어 시엔 false(스킵 없음).
		public readonly struct WaveStateChangedEvent
		{
				public readonly int CurrentWave;   // 1-based 현재 웨이브
				public readonly int TotalWaves;
				public readonly bool IsWaiting;
				public readonly float WaitSeconds;
				public readonly bool CanSkip;

				public WaveStateChangedEvent(int currentWave, int totalWaves, bool isWaiting, float waitSeconds, bool canSkip)
				{
						this.CurrentWave = currentWave;
						this.TotalWaves = totalWaves;
						this.IsWaiting = isWaiting;
						this.WaitSeconds = waitSeconds;
						this.CanSkip = canSkip;
				}
		}

		// 메인HUD 스킵 버튼 → DefenseStageMode 가 구독해 웨이브 대기를 즉시 종료한다(Defense 모드일 때만 유효).
		public readonly struct WaveSkipRequestedEvent
		{
		}

		// 스택 아이템 수량 변경 알림 (재료·소모품·수집품). 인벤토리 UI 등에서 구독.
		public readonly struct InventoryChangeEvent
		{
				public readonly int ItemId;
				public readonly int Count;

				public InventoryChangeEvent(int itemId, int count)
				{
						this.ItemId = itemId;
						this.Count = count;
				}
		}

		// 장비 인스턴스 변경 알림 (획득/소멸/강화/승급). Uid=0 이면 목록 전체를 다시 그리라는 뜻.
		public readonly struct EquipmentChangeEvent
		{
				public readonly long Uid;

				public EquipmentChangeEvent(long uid)
				{
						this.Uid = uid;
				}
		}

		// 장착 슬롯 변경 알림 (Uid=0 은 해제). 캐릭터는 하나뿐이므로 슬롯만 싣는다. 장비 UI 등에서 구독.
		public readonly struct PresetChangeEvent
		{
				public readonly EquipSlotTypes Slot;
				public readonly long Uid;

				public PresetChangeEvent(EquipSlotTypes slot, long uid)
				{
						this.Slot = slot;
						this.Uid = uid;
				}
		}

		// 캐릭터 변경 알림 (레벨업/경험치 변경). 캐릭터 정보 UI 등에서 구독.
		public readonly struct CharacterChangeEvent
		{
		}

		// 마스터리 변경 알림 (노드 투자 / 트리 초기화 / 레벨업).
		// 리졸브 캐시와 스탯 캐시를 함께 무효화해야 하는 지점이다 (스킬 설계 11.4).
		public readonly struct MasteryChangeEvent
		{
				public readonly WeaponMastery Mastery;

				public MasteryChangeEvent(WeaponMastery mastery)
				{
						this.Mastery = mastery;
				}
		}

		// 열린 창이 모두 닫혔을 때 알림 (UIManager가 마지막 창을 닫은 직후 발행).
		// 하단 탭 그룹 등에서 구독해 탭 선택을 해제한다.
		public readonly struct WindowClosedEvent
		{
		}

		// 소모품 사용 시도 결과. 실패해도 발행된다 — 쿨다운 표시·실패 안내가 같은 지점에서 갱신된다.
		public readonly struct ConsumableUsedEvent
		{
				public readonly int ItemId;
				public readonly Consumables.ConsumableUseResult Result;

				public ConsumableUsedEvent(int itemId, Consumables.ConsumableUseResult result)
				{
						this.ItemId = itemId;
						this.Result = result;
				}
		}

		// 퀘스트 상태 변경 (수락 / 진행도 갱신 / 완료 / 포기).
		// 퀘스트 마커와 HUD 는 이 이벤트로만 갱신한다 — 매 프레임 순회 금지 (퀘스트 설계 5.5).
		public readonly struct QuestChangeEvent
		{
				public readonly int QuestId;

				public QuestChangeEvent(int questId)
				{
						this.QuestId = questId;
				}
		}

		// 던전 단계 클리어. QuestTargetType.DungeonClear 판정의 입구다.
		// 최고 클리어 단계 갱신(DungeonProgress)과 같은 시점에 발행된다.
		public readonly struct DungeonStageClearedEvent
		{
				public readonly EDT.Dungeon DungeonType;
				public readonly int Stage;

				public DungeonStageClearedEvent(EDT.Dungeon dungeonType, int stage)
				{
						this.DungeonType = dungeonType;
						this.Stage = stage;
				}
		}
}