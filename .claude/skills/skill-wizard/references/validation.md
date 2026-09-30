# 요약 · 검증

3단계에서 아래 순서대로 보여 준다.

## 1. 새 행 표

행마다 표를 하나씩 만든다. 제목은 `#### Skill.xlsx #Skill — Skill_X` 형식으로 쓴다.

- 표는 `| 컬럼 | 값 |` 두 열로 만든다. 컬럼이 21개라 가로 표로는 읽기 어렵기 때문이다.
- 시트 헤더 순서대로 **모든 컬럼**을 싣는다. 비운 칸은 `(빈칸)` 으로 쓴다.
- 하위 행이 여럿이면 만든 순서대로 나열한다. 순서는 스킬 → 효과 → 버프·투사체·소환물이다.

## 2. 오류 — 하나라도 있으면 엑셀 추가 불가

| # | 조건 |
|---|---|
| 1 | Skill 의 `EffectID_01` 이 비어 있다 (`_02` 만 있어도 오류로 본다 — `_01` 부터 채운다) |
| 2 | `ScanType` 이 있는데 `ScanRange = 0` 이다. 예외: 소유자가 소환물이면 Radius 를 상속하므로 허용한다 |
| 3 | `SkillCategory = Normal` 인데 `Cooldown` 이 0 이거나 비어 있다 |
| 4 | CastingParam 이 범위를 벗어난다 (questions.md S8 표 — 확률 > 1, OnCombo 소수·0 이하, Aura·Casting 0 이하, OnLowHP 0 이하·1 초과) |
| 5 | `AnimLength > 0` 인데 `AnimName` 이 비어 있다 |
| 6 | `EffectTime` 이 0~1 을 벗어난다 |
| 7 | Buff 효과의 `Chance` 가 1 을 넘는다 |
| 8 | `Count` / `HitCount` / `TickCount` / `StackMax` 가 0 이하다 |
| 9 | Heal 의 `TickCount > 1` 인데 `TickInterval` 이 0 이다 (2틱 이후가 사라진다) |
| 10 | `ScaleStat` 에 `StatDetail_` 값이 들어갔다, 또는 StatChange `StatDetailID` 에 `Stat_` 값이나 `_Base` 레이어가 들어갔다 |
| 11 | CooldownReduce 의 `TargetSkillID` 가 비었다, 또는 `Ratio` 와 `FlatValue` 가 모두 0 이다 |
| 12 | 필수 참조 슬롯이 비어 있다 (Buff·Projectile·Summon·BuffConsume `RefID`, Force `ForceType`) |
| 13 | ID 가 기존 ID 나 대기 목록과 중복된다 |
| 14 | 참조한 ID 가 기존 목록에도 대기 목록에도 없다 |

## 3. 경고 — 표시만 하고 추가는 허용

| # | 조건 | 안내 문구 |
|---|---|---|
| 1 | CastingType 이 `OnDamaged` / `OnKill` 이다 | 현재 트리거 호출처가 없어 발동하지 않는다 |
| 2 | 소유자가 무기·몬스터·보스이고, `SkillCategory ≠ Normal`, CastingType 이 Instant/Casting 인데 `ScanType` 이 비어 있다 | AI 가 자동 시전하지 않는다 |
| 3 | CastingType 이 `OnHit` / `OnCrit` 이다 | 시전자의 다른 Damage 효과(보통 평타)에 `OnHitTrigger=TRUE` 가 있어야 발동한다 |
| 4 | `HitCount > 1` 인데 `OnHitTrigger = TRUE` 다 | 온히트·흡혈이 타격 수만큼 반복된다 |
| 5 | Force 효과가 Skill `EffectID_01/_02` 에 직접 연결됐다 | 빗나가도 밀려난다 — ChainEffectIDs 권장 |
| 6 | BuffConsume 인데 `ChainEffectIDs` 가 비어 있다 | 소모만 하고 아무 일도 없다 |
| 7 | Location 효과가 있는데 스킬 ScanType 이 Sector/Line 이다 | ScanParam(각도/폭)이 재탐색 반경이 된다 |
| 8 | 소모품 스킬인데 CastingType 이 Instant/Casting 이 아니다 | 소모품으로 발동하지 않는다 (ConsumableCatalog 경고) |
| 9 | 보스 스킬인데 CastingType 이 Casting 이 아니다 | `BossMonsterPhase.PhaseSkillID` 로 쓰려면 Casting 이어야 한다 |
| 10 | 소환물 스킬인데 `SkillCategory = Normal` 이다 | 공속을 참조한다 — 소환물이 공속을 상속하지 않으면 주기가 붕괴한다 |
| 11 | Summon `AIType = Chase` 인데 `ReturnDistance` 가 0 이다 | 무한히 추격한다 |
| 12 | Summon `SkillID_1` 이 비어 있다 | 아무 행동도 하지 않는다 |
| 13 | Summon `ScaleByRadius = TRUE` 인데 그 소환물을 만드는 Summon 효과의 `Radius` 가 0 이다 | 크기가 0 이 된다 |
| 14 | Buff 의 `BlockFlags` 와 `EffectID_01/_02` 가 모두 비어 있다 | 아무 효과도 없는 버프다 |
| 15 | Buff `TickInterval > 0` 인데 Damage/Heal 효과가 없다 | 틱마다 할 일이 없다 |
| 16 | Projectile / Summon 의 `PrefabPath` 가 비어 있다 | 생성에 실패한다 — 수동으로 기입해야 한다 |
| 17 | Projectile 의 `HitEffectID_1` 이 비어 있다 | 맞아도 아무 일 없다 |

## 4. 획득 경로 안내

마법사는 스킬 행만 만든다. 게임에서 이 스킬을 쓰려면 아래 중 한 곳에 등록해야 한다. 등록하지 않으면 부팅 시 "참조되지 않는 스킬" 경고가 뜬다.

| 소유자 | 등록할 곳 |
|---|---|
| 무기 — 평타 | `Mastery.xlsx` `#WeaponMastery.NormalAttackSkill` |
| 무기 — 액티브·패시브 | `Option.xlsx` `#Option` 에 `OptionType=SkillGrant`, `OptionTarget=스킬 ID` 행을 만든다 → `Mastery.xlsx` `#SkillTreeNode.Option` 에 연결한다 |
| 몬스터 | `Monster.xlsx` `#MonsterSkillSet.SkillID` — 스킬셋 그룹마다 Normal 스킬이 1개 필요하다 |
| 보스 | `Monster.xlsx` `#BossMonsterPhase.PhaseSkillID` 또는 `#MonsterSkillSet` |
| 소환물 | 이번에 만든(또는 기존) `Summon.SkillID_1/_2` — 마법사 안에서 연결했다면 이미 완료 |
| 소모품 | `Consumable.xlsx` `#Consumable` 의 `ConsumeEffect=Skill`, `EffectParam_1=스킬 ID` |
| 균열 | `Map.xlsx` `#RiftSkill.SkillID` |

## 5. 마지막 질문

```
[확인] 위 내용으로 진행할까요?
1. 엑셀에 추가
2. 항목 수정
3. 취소
```

오류가 있으면 1 을 `1. (오류 수정 전 추가 불가)` 로 표시한다. 사용자가 1 을 입력하면 오류 목록을 다시 보여 준다.
