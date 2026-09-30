# 질문 트리

## 표기

| 표기 | 의미 |
|---|---|
| **필수 입력** | 값을 바로 받는다. 패스 없음 |
| **입력/패스** | `1. 입력` / `2. 패스(…)` 를 먼저 묻는다. 패스하면 빈칸 |
| **선택** | 번호 목록에서 고른다 |
| **생략** | 조건에 해당하면 묻지 않고 빈칸으로 둔다 |

- 숫자 검증에 실패하면 이유를 한 줄로 알리고 다시 묻는다.
- bool 은 `1. TRUE` / `2. FALSE` 로 묻는다. FALSE 는 빈칸으로 기록한다.
- 질문 번호(S1, E3 …)는 태그에 그대로 쓴다. 예: `[Skill 7]`, `[Effect 3]`. 생략된 번호는 건너뛴다.

## 문답 중 기억할 문맥

| 이름 | 값 |
|---|---|
| `owner` | 무기 / 몬스터 / 보스 / 소환물 / 소모품 / 균열 / 기타 (S3) |
| `rootSkill` | 지금 만들고 있는 스킬 행. 중첩된 경우 스택에서 가장 가까운 스킬 |
| `base` | `rootSkill` ID 에서 `Skill_` 을 뗀 문자열. 하위 ID 를 제안할 때 쓴다 |
| `ctx` | 효과 문맥. `slot`(Skill.EffectID) / `chain`(ChainEffectIDs) / `hit`(Projectile.HitEffectID) / `buff`(Buff.EffectID) |

**ID 제안 규칙 (공통)**
- 제안 ID 가 기존 ID 나 대기 목록과 겹치면 뒤에 `_02`, `_03` … 을 붙인다.
- 질문은 `1. 사용` / `2. 직접 입력` 으로 묻는다.
- 직접 입력한 값은 다음을 검증한다.
  - 접두가 맞는가
  - 영문·숫자·`_` 만 쓰였는가
  - 기존 ID 나 대기 목록과 중복되지 않는가

**기존 ID 선택 질문 (공통)**
- inspect 의 ID 목록에 대기 목록(`(신규)` 표시)을 더해 번호를 매긴다.
- 효과 목록은 `[EffectType]` 을 함께 보여 준다.
- 번호 대신 ID 를 직접 입력해도 받는다.

---

## A. Skill — `Skill.xlsx` `#Skill`

### S1. Name — 필수 입력
`스킬 이름을 입력해 주세요. (게임에 표시되는 이름)`

### S2. SkillCategory — 선택
```
1. Normal - 기본공격. 공속이 쿨다운·모션에 적용되고, 적중할 때마다 콤보 카운트 +1
2. Active - 액티브
3. Passive - 패시브 (분류 표시용 — 상시 적용 여부는 [Skill 7] CastingType=Passive 가 결정)
```
- 코드는 Normal 인지 아닌지만 구분한다.
- 몬스터 스킬셋에는 Normal 스킬이 정확히 1개 있어야 한다. AI 가 전부 실패했을 때 쓰는 폴백이다.
- 소환물 스킬은 Active 를 권장한다. Normal 은 공속을 참조하는데 소환물이 공속을 상속하지 못할 수 있다.

### S3. 소유자 — 선택 (ID 제안용)
```
1. 무기 마스터리 (히어로 무기 스킬)
2. 몬스터
3. 보스
4. 소환물 (소환물이 사용하는 스킬)
5. 소모품 (아이템 사용 효과)
6. 균열 던전 스킬
7. 기타 (ID 직접 입력)
```

| 선택 | 이어서 묻는 것 | 제안 ID |
|---|---|---|
| 1 무기 | WeaponType 선택 (inspect 의 `WeaponType`) | Normal: `Skill_{W}_Attack` / Active: `Skill_{W}_Active_{NN}` / Passive: `Skill_{W}_Passive_{NN}` |
| 2 몬스터 | 종류 영문 입력 (예: `MeleeAttack`, `CastAttack`, `OrcGeneral_Active`) | `Skill_Monster_{종류}_{NN}` |
| 3 보스 | 보스 이름 영문 입력 (예: `Doom`) | `Skill_Monster_Boss_{이름}_{NN}` |
| 4 소환물 | 소환물 이름 영문 (예: `SummonSpark`) + 동작 영문 (예: `Attack`) | `Skill_{소환물}_{동작}` |
| 5 소모품 | 효과 이름 영문 (예: `HealSmall`) | `Skill_Item_{효과}` |
| 6 균열 | 이름 영문 (예: `Burst`) | `Skill_Rift_{이름}` |
| 7 기타 | — | S4 에서 직접 입력 |

- `NN` 은 같은 접두를 가진 기존 ID·대기 목록의 가장 큰 번호 + 1 이다. 두 자리로 쓴다. 없으면 `01` 이다.
- 무기 Normal 인데 `Skill_{W}_Attack` 이 이미 있으면 이렇게 안내한 뒤, S4 에서 직접 입력만 제시한다.
  - "평타는 무기당 1개다(`WeaponMastery.NormalAttackSkill`). 이미 있다."
- 소환물 하위 문답(M8/M9)에서 들어왔다면 소유자는 4 로 고정하고 S3 을 묻지 않는다.
  - 소환물 이름은 Summon ID 에서 `_` 를 뺀 값으로 제안한다. 예: `Summon_Spark` → `SummonSpark`.
  - 동작만 입력받는다.

### S4. ID — `1. 사용` / `2. 직접 입력`
`[Skill 4] 스킬 ID 를 확인해 주세요. 제안: Skill_DualBlades_Active_03`
- 검증: `Skill_` 로 시작, 영문·숫자·`_` 만, 중복 없음.

### S5. Desc — 입력/패스
- 질문: `스킬 설명(Desc)을 입력해 주세요.`
- 패스: 비워둠 — 현재 균열 UI 에서만 표시된다.

### S6. Icon — 입력/패스
- 질문: `아이콘 스프라이트 주소를 입력해 주세요. (예: skill_001)`
- 패스: 비워둠 — 현재 균열 UI 에서만 쓰이고, 비어 있으면 아이콘이 숨겨진다.

### S7. CastingType — 선택
```
1. Instant - 즉시 발동. AI·직접 시전 가능
2. Casting - 시전 시간 경과 후 발동 (다음: 시전 시간). 모션은 시전 완료 순간에 나오고 대상은 발동 시점에 다시 찾는다
3. OnHit - 공격 적중 시 확률 발동 (다음: 확률)
4. OnCombo - n번째 평타 적중마다 발동 (다음: 횟수)
5. OnCrit - 치명타 시 확률 발동 (다음: 확률)
6. OnDamaged - 피격 시 확률 발동 ⚠ 현재 호출처가 없어 발동하지 않음
7. OnLowHP - 체력이 일정 비율 이하일 때 발동 (다음: 비율, 0.5초마다 검사)
8. OnKill - 적 처치 시 확률 발동 ⚠ 현재 호출처가 없어 발동하지 않음
9. Aura - 주기마다 스킬 전체를 재실행 (다음: 주기). Cooldown 무시
10. Passive - 등록 시 1회 상시 적용. 모션·VFX·SFX·Cooldown 미사용
```

`(권장)` 표시 기준

| 조건 | 권장 |
|---|---|
| S2 = Passive | 10 |
| S2 = Normal | 1, 2 |
| 소유자 = 소모품 | 1, 2 — 나머지에는 `⚠ 소모품은 Instant/Casting 만` 표시 |
| 소유자 = 균열 | 1 |
| 소유자 = 보스 | 2 — 보스 페이즈 스킬(`PhaseSkillID`)은 Casting 이어야 한다 |

- 트리거형(3~8)은 직접 시전할 수 없다. 각자의 조건으로만 발동한다.
- 자동 전투에서 무기 액티브는 보통 조건 발동형(OnCombo 등)을 쓴다.

### S8. CastingParam — CastingType 에 따라 분기

| CastingType | 질문 | 방식 | 검증 |
|---|---|---|---|
| Instant, Passive | — | **생략** | |
| Casting | 시전 시간(초). 공속과 무관하고, 취소돼도 쿨다운은 소모된다 | 필수 입력 | > 0 |
| OnHit, OnCrit | 발동 확률 (0~1, 25% = 0.25) | 필수 입력 | 0 < x ≤ 1 |
| OnDamaged, OnKill | 발동 확률 (0~1) | 필수 입력 | 0 < x ≤ 1 |
| OnCombo | 몇 번째 평타 적중마다 발동할지 (정수) | 필수 입력 | 정수 ≥ 1 |
| OnLowHP | 발동 체력 비율 (0~1, 30% = 0.3) | 필수 입력 | 0 < x ≤ 1 |
| Aura | 재실행 주기(초) | 필수 입력 | > 0 |

- OnHit / OnCrit 을 고르면 다음을 안내한다.
  - "시전자가 가진 Damage 효과 중 `OnHitTrigger=TRUE` 인 것이 적중해야 발동한다. 보통 평타 데미지가 그 역할을 한다."

### S9. ApplyTarget — 선택
```
1. Self - 시전자 자신. 탐색하지 않음
2. Enemy - 적 진영 (시전자 제외)
3. Friendly - 아군 진영 (시전자 포함 — 자힐·자버프)
4. All - 모두
```

### S10. ScanType — 조건 분기

**생략 조건**: 아래를 모두 만족하면 S10~S12 를 생략한다. ScanType·ScanRange·ScanParam 은 모두 빈칸이 된다.
- ApplyTarget = Self
- CastingType 이 Passive·Aura·트리거형(3~8) 중 하나이거나, 소유자가 소모품·균열

**Self 이면서 Instant/Casting 인 경우**에는 먼저 이렇게 안내한다.
- "AI 는 ApplyTarget 과 무관하게 적이 ScanType/ScanRange 범위에 들어와야 시전한다. AI 가 이 자기 버프를 쓰게 하려면 감지용 탐색을 넣는다."
- 그런 다음 아래 목록을 보여 준다. 이때는 5 의 ⚠ 설명을 "AI 자동 시전 안 됨" 으로 바꾼다.

```
1. Circle - 원형. ScanRange = 반경
2. Sector - 부채꼴. ScanRange = 반경, ScanParam = 전체 각도(도)
3. Line - 직선. ScanRange = 길이, ScanParam = 전체 폭
4. Target - 가까운 순 N명. ScanRange = 사거리, ScanParam = 최대 대상 수
5. None - 탐색 안 함 ⚠ Target 효과가 아무도 못 맞히고 AI 가 시전하지 않음
```
- 범위형(Circle / Sector / Line)은 범위 안의 적을 전부 때린다. 대상 수 제한은 Target 에만 있다.

### S11. ScanRange — ScanType 이 None 이 아닐 때만 묻는다. 필수 입력
- 질문 문구는 ScanType 에 따라 다르다: Circle·Sector 는 `반경`, Line 은 `길이`, Target 은 `사거리`.
- 검증: > 0.
  - 예외: 소유자가 소환물이면 `0` 을 허용한다. 질문에 이렇게 덧붙인다: "0 을 입력하면 소환 효과의 Radius 를 상속한다(장판 크기를 한 곳에서 정의)."

### S12. ScanParam — ScanType 에 따라 분기

| ScanType | 방식 | 질문 | 검증 |
|---|---|---|---|
| Circle | **생략** (단, 나중에 Location 효과가 생기면 E3 에서 여기로 돌아와 묻는다) | | |
| Sector | 필수 입력 | 부채꼴 전체 각도(도). 360 = 전방위 | 0 < x ≤ 360 |
| Line | 필수 입력 | 직선 전체 폭 | > 0 |
| Target | 입력/패스 (패스 = 1명) | 최대 대상 수 | 정수 ≥ 1 |

### S13. AnimName — CastingType = Passive 이면 생략. 선택
```
1. Attack - 평타 모션
2. Skill - 스킬 모션
3. Hit - 피격 모션
4. Die - 사망 모션
5. 패스 - 모션 없이 효과만 (평타 모션 위에 효과를 얹는 OnCombo 스킬 등)
```
- 애니메이터 **트리거명**이다. 이 4개 외의 값은 아무 로그 없이 무시되므로 직접 입력은 받지 않는다.
- 권장: Normal → 1, Active 이면서 Instant/Casting → 2, OnCombo → 5.
- Casting 은 시전을 마친 순간에 모션을 낸다.

### S14. AnimLength — AnimName 이 있을 때만 묻는다. 입력/패스
- 질문: `애니메이션 클립의 실제 길이(초, 공속 1.0 기준)를 입력해 주세요.`
- 이 값이 모션 락과 효과 발동 시점(EffectTime)의 기준이 된다. 밸런스 조정용으로 바꾸지 않는다.
- 패스: 0 — 모든 효과가 즉시 발동하고 모션 락이 없다.
- 검증: > 0.

### S15. Cooldown
- **생략**: CastingType 이 Passive·Aura 이거나, 소유자가 소모품·균열이면 생략한다. 이 경로들에서는 쿨다운이 적용되지 않는다.
- Normal: 필수 입력.
  - 질문: `공격 주기(초)를 입력해 주세요. 실제 주기 = Cooldown / 공속`
  - 검증: > 0.
- 그 외: 입력/패스.
  - 질문: `재사용 대기시간(초)을 입력해 주세요. 트리거형은 발동 후 내부 쿨다운으로 쓰인다.`
  - 패스: 0 — 쿨다운 없음(모션 락만 걸린다).
  - OnLowHP 에서 패스하면 확인한다: "⚠ 체력 조건이 유지되는 동안 0.5초마다 재발동한다."

### S16. SkillVFX — CastingType = Passive 이면 생략. 입력/패스
- 질문: `시전 연출 VFX 이름을 입력해 주세요. (시전 위치에 1회 재생, 시전자에 붙지 않음)`
- 입력하면 이어서 묻는다.
  - **S16-1 SkillVFXFacing** — 선택: `1. TRUE - 조준 방향으로 회전` / `2. FALSE - 회전 없음`
  - **S16-2 SkillVFXOffset** — 입력/패스: 조준 방향으로 이만큼 앞에 낸다. 패스하면 0 이다.
- 패스하면 S16-1, S16-2 는 생략한다.

### S17. SkillSFX — CastingType = Passive 이면 생략. 입력/패스
- 질문: `시전 사운드 이름을 입력해 주세요.`

### S18. BreakDamage — 소유자가 몬스터·보스이거나 CastingType = Passive 이면 생략. 입력/패스
- 질문: `엘리트·보스의 브레이크 게이지를 피격 1회당 깎는 양을 입력해 주세요. (가이드: 평타 1, 액티브 5)`
- 몬스터·보스를 생략하는 이유: 히어로는 브레이크 대상이 아니다.
- Passive 를 생략하는 이유: 등록 시 1회 적용되는 상시 효과라 적을 타격하지 않는다.
- 패스: 0 — 브레이크에 관여하지 않는다.

### S19. EffectID_01 — 선택 (필수)
```
1. 기존 효과 선택
2. 새 효과 생성
```
- 1 을 고르면 기존 ID 선택 질문으로 넘어간다.
- 2 를 고르면 **B. Effect** 를 `ctx=slot` 으로 시작한다.

### S20. EffectID_02 — 선택
```
1. 기존 효과 선택
2. 새 효과 생성
3. 없음
```
- "_02 는 발동할 때 무조건 실행된다. 적중했을 때만 붙일 효과(경직·넉백 등)는 효과의 ChainEffectIDs 로 연결한다" 고 안내한다.
- 고른 효과가 Force 이면 다시 묻는다.
  - "⚠ 빗나가도 밀려난다. Force 는 ChainEffectIDs 로 연결을 권장"
  - `1. 그대로 두기` / `2. 다시 선택`

→ S20 까지 끝나면 A 는 완료다. 스택이 비었으면 3단계 요약으로 가고, 아니면 스택으로 복귀한다.

---

## B. Effect — `Skill.xlsx` `#SkillEffect`

### E1. EffectType — 선택
```
1. Damage - 피해
2. Heal - 회복
3. Buff - 버프/디버프 부여 (새 버프 생성 가능)
4. StatChange - 스탯 직접 변경 (한시/영구)
5. Projectile - 투사체 발사 (새 투사체 생성 가능)
6. Summon - 소환물 생성 (새 소환물 생성 가능)
7. Force - 밀기/당기기
8. CooldownReduce - 쿨타임 회복
9. BuffConsume - 버프 스택 소모 (조건 분기 게이트)
```

| 문맥 | 보충 표시 |
|---|---|
| `ctx=slot` | 7 에 `⚠ 빗나가도 밀려남 — ChainEffectIDs 로 연결 권장` |
| `ctx=chain` | 7 에 `(권장)` |
| `ctx=hit` | 1 에 `(권장)` |
| `ctx=buff` | 1·2·4 에 `(권장)` — DoT / HoT / 스탯 버프 |

### E2. ID — `1. 사용` / `2. 직접 입력`
- 제안: `SE_{base}_{EffectType}`.
  - `ctx=buff` 이면 base 는 버프 ID 에서 `BUFF_` 를 뗀 값이다.
- 검증: 접두 `SE_`.

### E3. EffectOrigin — 선택
- **생략**: EffectType = CooldownReduce 이면 묻지 않고 `Caster` 로 기록한다(고정).

```
1. Caster - 시전자 자신 (자기 버프·회복·소환·투사체 발사)
2. Target - 탐색된 대상 (일반 피해·디버프)
3. Attacker - ⚠ 코드상 Target 과 동일하게 처리
4. Victim - ⚠ 코드상 Target 과 동일하게 처리
5. Owner - 소환물의 주인 (주인이 없으면 시전자)
6. Location - 시전 시점 좌표에 고정 → 발동 시 그 좌표에서 ScanParam 반경으로 다시 찾음
```

**선택지 노출 조건**
- 5 Owner: 소유자가 소환물일 때만 보여 준다.
- 6 Location: `ctx=slot` 일 때만 보여 준다. 다른 문맥에서는 Target 과 같다.
- 번호는 노출된 선택지 순서대로 다시 매긴다.

**`(권장)` 표시 기준**

| 조건 | 권장 |
|---|---|
| `ctx=hit` / `ctx=buff` | Target — 대상이 맞은 유닛 / 버프 보유자다 |
| Damage, Force, 디버프 Buff | Target |
| Heal, StatChange, 자기 강화 Buff, Summon, BuffConsume | Caster (아군 회복은 Target + ApplyTarget=Friendly) |
| Projectile | Caster — 기존 데이터 방식이다 |

Projectile 의 두 선택지 차이도 함께 안내한다.
- Caster: 시전자 위치에서 발사한다.
- Target: 가장 가까운 탐색 대상을 조준한다.

**Location 을 고르면** `rootSkill` 의 ScanType 에 따라 추가로 처리한다.

| ScanType | 처리 |
|---|---|
| Circle, ScanParam 비어 있음 | `[Skill 12] Location 효과의 재탐색 반경(ScanParam)을 입력해 주세요.` 를 즉시 묻는다. 필수, > 0. 스킬 행에 기록한 뒤 E4 로 진행한다 |
| Target | "ScanParam(현재 N)이 최대 대상 수이자 재탐색 반경으로 함께 쓰인다(예: 1.25 → 1명, 반경 1.25)" 라고 안내한다 |
| Sector, Line | `⚠ ScanParam(각도/폭)이 그대로 재탐색 반경이 된다(120도 → 반경 120)` 를 보여 주고 `1. 그대로` / `2. 다른 Origin 선택` 을 묻는다 |
| None / Self | "탐색 결과가 비면 좌표를 잡지 못해 헛시전한다(Self 면 시전자 위치)" 라고 안내한다 |

### E4. EffectTime — 입력/패스
- **묻는 조건**: 아래를 모두 만족할 때만 묻는다. 하나라도 아니면 생략하며, 이 경우 코드가 EffectTime 을 무시한다.
  - `ctx=slot`
  - `rootSkill` 의 AnimLength > 0
  - 소유자가 소모품·균열이 아님
- 질문: `모션 중 발동 시점 비율(0~1)을 입력해 주세요. 발동 지연 = (캐스팅 시간) + 동작시간 × EffectTime`
- 패스: 0 — 즉시 발동한다. 캐스팅형은 캐스팅이 끝나는 즉시 발동한다.
- 검증: 0 ≤ x ≤ 1.

### E5. EffectParam_1~5 — EffectType 에 따라 분기

inspect 의 `#SkillParamDef` 를 기준으로 슬롯 순서대로 묻는다. 태그는 `[Effect 5-N]` 이다. `(미사용)` 슬롯은 생략한다.

| EffectType | 슬롯 | ParamKey | 방식 | 안내 / 검증 |
|---|---|---|---|---|
| Damage | 1 | Ratio | 필수 입력 | ScaleStat × Ratio. 2.5 = 250% |
| | 2 | FlatValue | 입력/패스 (0) | 고정 추가 피해 |
| | 3 | HitCount | 입력/패스 (1) | 다단 타격 횟수. 정수 ≥ 1 |
| | 4 | HitInterval | HitCount > 1 일 때만 입력/패스 (0 = 같은 프레임에 반복) | 초, 공속 무관 |
| | 5 | ScaleStat | 선택 — Stat 목록, `Stat_Atk` 을 1번에 두고 (권장) | `StatDetail_` 은 불가 |
| Heal | 1 | Ratio | 필수 입력 | |
| | 2 | FlatValue | 입력/패스 (0) | |
| | 3 | TickCount | 입력/패스 (1) | 정수 ≥ 1 |
| | 4 | TickInterval | TickCount > 1 이면 **필수** | > 0. 0 이면 2틱 이후가 사라진다 |
| | 5 | ScaleStat | 선택 — `Stat_MaxHp` / `Stat_Atk` 을 앞에 둔다 | |
| Buff | 1 | RefID | 선택 — `1. 기존 버프 선택` / `2. 새 버프 생성`(→ **E. Buff**) | 필수 |
| | 2 | Duration | 입력/패스 (비움 = 무한) | 초 |
| | 3 | StackMax | 입력/패스 (1) | 정수 ≥ 1 |
| | 4 | Ratio | 입력/패스 (1.0) | 버프가 참조하는 효과 수치의 배율 |
| | 5 | Chance | 입력/패스 (1.0 = 항상) | 0 < x ≤ 1 |
| StatChange | 1 | StatDetailID | 2단계 선택 — Stat 선택 → 레이어 선택 (그 Stat 에 존재하는 Add/Ratio/Amp 만, Base 제외) | 필수. `Stat_` 불가 |
| | 2 | Value | 필수 입력 | Percent 스탯은 0~1 배율 (0.1 = 10%) |
| | 3 | Duration | `ctx=buff` 면 **생략**(버프 수명에 맞춰 회수), 그 외 입력/패스 (0 = 영구) | 초 |
| Projectile | 1 | RefID | 선택 — `1. 기존 투사체 선택` / `2. 새 투사체 생성`(→ **C. Projectile**) | 필수 |
| | 2 | Count | 입력/패스 (1) | 정수 ≥ 1 |
| | 3 | Angle | Count > 1 일 때만 입력/패스 (0) | 발사체 사이 분산각(도) |
| | 4 | Interval | Count > 1 일 때만 입력/패스 (0 = 동시 부채꼴) | 연속 발사 간격(초) |
| | 5 | SpeedRate | 입력/패스 (1) | > 0 |
| Summon | 1 | RefID | 선택 — `1. 기존 소환물 선택` / `2. 새 소환물 생성`(→ **D. Summon**) | 필수 |
| | 2 | Count | 입력/패스 (1) | 정수 ≥ 1. 동시 상한은 Summon.MaxCount |
| | 3 | Duration | 입력/패스 (0 = 영구) | 초 |
| | 4 | Radius | 입력/패스 (0). 소환물이 `ScaleByRadius=TRUE` 이거나 소환물 스킬의 `ScanRange=0` 이면 **필수** | 배치·공전·장판 반경. 소환물 스킬의 ScanRange=0 을 이 값이 채운다 |
| Force | 1 | Power | 필수 입력 | 밀기/당기기 거리 |
| | 2 | Duration | **생략** — 코드에서 쓰지 않음 | |
| | 3 | ForceType | 선택 — `1. Push - 밀어냄` / `2. Pull - 당김` | 필수 |
| CooldownReduce | 1 | TargetSkillID | 선택 — `1. 이 스킬 자신({rootSkill ID})` / `2. 기존 스킬 선택` | 필수. 시전자 자신이 가진 그 스킬의 쿨이 줄어든다 |
| | 2 | Ratio | 입력/패스 (0) | 최대 쿨타임 대비 비율. 1.0 = 초기화 |
| | 3 | FlatValue | 입력/패스 (0) | 초. Ratio 와 FlatValue 가 둘 다 0 이면 다시 묻는다 |
| BuffConsume | 1 | RefID | 선택 — `1. 기존 버프 선택` / `2. 새 버프 생성` | 필수 |
| | 2 | Count | 입력/패스 (1) | 소모할 스택 수. 정수 ≥ 1 |

inspect 의 ParamDef 가 이 표와 다르면 inspect 를 따른다. 슬롯이 새로 생기거나 바뀐 경우, 그 슬롯은 `ParamKey(ValueType; 설명)` 을 보여 주고 입력/패스로 묻는다.

### E6. ChainEffectIDs — 선택
```
[Effect 6] 이 효과가 적중(성공)했을 때만 이어서 발동할 효과를 추가할까요?
1. 추가
2. 없음
```
- 1 을 고르면 `1. 기존 효과 선택` / `2. 새 효과 생성`(→ B, `ctx=chain`) 을 묻는다.
- 하나를 추가할 때마다 `1. 더 추가` / `2. 완료` 를 묻는다.
- 연쇄 효과는 EffectTime 을 무시하고 부모가 발동한 즉시 발동한다. 깊이 5 에서 끊긴다.
- "적중" 의 의미는 EffectType 마다 다르다.
  - Buff: Chance 판정을 통과한 경우
  - BuffConsume: 스택 소모에 성공한 경우
  - Heal: 회복량이 0 보다 큰 경우
- Force 는 이 칸으로 연결한다.
- EffectType = BuffConsume 인데 2(없음)를 고르면 확인한다.
  - "⚠ 소모만 하고 아무 일도 없다. 조건 분기로 쓰려면 본 효과를 연결한다."
  - `1. 추가` / `2. 그대로`

### E7. OnHitTrigger — EffectType = Damage 일 때만 묻는다. 선택
```
1. TRUE - 이 타격이 OnHit/OnCrit 스킬 발동과 흡혈(Stat_LifeOnHit)의 기점이 된다 (평타 데미지에 사용)
2. FALSE - 기본값
```
- HitCount > 1 인데 TRUE 를 고르면 확인한다.
  - "⚠ 타격 횟수만큼 온히트·흡혈이 반복 발동한다."
  - `1. 그대로` / `2. FALSE 로`

### E8. EffectVFX — 입력/패스
- 질문: `타격 연출 VFX 이름을 입력해 주세요.`
- 대상마다 재생된다. Location 이면 좌표에서 1회 재생된다.

### E9. EffectSFX — 입력/패스
- 질문: `타격 사운드 이름을 입력해 주세요.`

---

## C. Projectile — `Projectile.xlsx` `#Projectile`

속도·수명·사거리·궤적은 테이블에 없다. 모두 **프리팹의 Projectile 컴포넌트**가 소유한다.

| # | 컬럼 | 방식 | 안내 / 검증 |
|---|---|---|---|
| P1 | ID | `1. 사용` / `2. 직접` — 제안 `PJT_{base}` | 접두 `PJT_` |
| P2 | PrefabPath | 입력/패스 | 투사체 프리팹 어드레서블 주소 (예: `Prefab_Projectile_CrossBow_Bolt`). 패스하면 `⚠ 비어 있으면 발사 실패 — 나중에 수동 기입` |
| P3 | Pierce | 입력/패스 (0 = 첫 적중에 소멸) | 서로 다른 대상을 관통하는 횟수. 정수 ≥ 0 |
| P4 | ExplodeRadius | 입력/패스 (0 = 충돌 대상 1체만) | 0 보다 크면 착탄 지점 반경 안의 적 전부에 적중 효과가 적용된다 |
| P5 | HitEffectID_1 | 선택 — `1. 기존 효과` / `2. 새 효과 생성`(→ B, `ctx=hit`) / `3. 없음 ⚠ 맞아도 아무 일 없음` | |
| — | HitEffectID_2 | **생략** — 코드에서 쓰지 않음 | |

---

## D. Summon — `Summon.xlsx` `#Summon`

돌아오거나 머무는 물체는 투사체가 아니라 소환물이다. 장판도 소환물로 만든다: `AIType=Stationary` 소환물에 `CastingType=Aura` 스킬을 붙인다.

| # | 컬럼 | 방식 | 안내 / 검증 |
|---|---|---|---|
| M1 | ID | `1. 사용` / `2. 직접` — 제안 `Summon_{base}` | 접두 `Summon_` |
| M2 | PrefabPath | 입력/패스 | 프리팹에 `SummonUnit`·`UnitMover`·`UnitAnimator` 가 있어야 한다(없으면 생성이 거부된다). 패스하면 ⚠ |
| M3 | AIType | 선택 (inspect `SummonAIType`) | Stationary = 포탑·장판 / Follow = 따라다님 / Chase = 적 추격 / Wander = 배회(전투 미참여) / Orbit = 공전 |
| M4 | ScaleByRadius | 선택 `1. TRUE - 소환 Radius 에 맞춰 프리팹 자동 스케일 (장판 필수)` / `2. FALSE` | TRUE 면 부모 Summon 효과의 Radius 가 필수가 된다 |
| M5 | DieWithOwner | 선택 `1. TRUE - 주인 사망 시 소멸` / `2. FALSE` | |
| M6 | ReturnDistance | AIType = Chase 일 때만 **필수** | 주인에게서 이 거리 이상 벌어지면 복귀한다. > 0 (0 이면 무한 추격) |
| M7 | FollowDistance | Stationary 면 **생략**, 그 외 입력/패스 | 의미: Follow = 추종 거리 / Chase = 복귀 위치 / Wander = 배회 반경 / Orbit = 공전 반경 |
| M8 | SkillID_1 | 선택 — `1. 기존 스킬` / `2. 새 스킬 생성`(→ A, 소유자 = 소환물) / `3. 없음 ⚠ 아무 행동도 안 함` | 장판이면 새 스킬은 Aura + ScanRange 0 (Radius 상속) |
| M9 | SkillID_2 | 선택 — `1. 기존` / `2. 새로` / `3. 없음` | |
| — | ActionInterval | **생략** — 코드에서 쓰지 않음 | |
| M10 | InheritStatType_1 | 선택 — Stat 목록 + `없음`. `Stat_Atk` 을 앞에 두고 (권장) | 주인 스탯 상속, 실시간 반영 |
| M10-1 | InheritStatRatio_1 | M10 을 골랐으면 **필수** | 1 = 100% |
| M11 | InheritStatType_2 | 선택 — Stat 목록 + `없음` | |
| M11-1 | InheritStatRatio_2 | M11 을 골랐으면 **필수** | |
| M12 | MaxCount | 입력/패스 (0 = 무제한) | 동시 존재 상한. 넘치면 오래된 것부터 소멸한다 |

---

## E. Buff — `Buff.xlsx` `#Buff`

지속시간·최대 중첩·배율은 Buff 테이블이 아니라 **버프를 부여하는 SkillEffect(Buff 타입)의 Duration / StackMax / Ratio** 가 정한다.

| # | 컬럼 | 방식 | 안내 / 검증 |
|---|---|---|---|
| B1 | ID | `1. 사용` / `2. 직접` — 제안 `BUFF_{base}` | 접두 `BUFF_` |
| B2 | Name | 필수 입력 | 표시명 |
| B3 | Desc | 입력/패스 | |
| B4 | Icon | 입력/패스 | HUD 버프 아이콘 스프라이트 주소 (예: `skill_018`) |
| B5 | IsDebuff | 선택 `1. TRUE - 디버프(정화 대상)` / `2. FALSE - 버프` | |
| B6 | StackPolicy | 선택 (inspect `BuffStackPolicy`, 설명 포함) | 같은 버프가 다시 걸릴 때의 정책 |
| B7 | TickInterval | 입력/패스 (0 = 부착 시 1회 적용되는 지속형) | 0 보다 크면 그 주기로 효과를 반복한다(DoT·HoT) |
| B8 | BlockFlags | 번호를 쉼표로 여러 개 입력 — `1. Move` `2. Turn` `3. Attack` `4. Cast` `5. 없음` | 참고: 기절 = 1,2,3,4 / 경직 = 1,3,4. 결과는 `Move;Attack;Cast` 형식 |
| B9 | IgnoreImmune | 선택 `1. TRUE - 무적·면역 무시 (보스 파훼 기절 등)` / `2. FALSE` | |
| B10 | DebuffMotion | 선택 `1. TRUE - 피격(경직) 모션 재생` / `2. FALSE` | |
| B11 | EffectID_01 | 선택 — `1. 기존 효과` / `2. 새 효과 생성`(→ B, `ctx=buff`) / `3. 없음` | 버프 효과의 대상은 버프 보유자다(Origin = Target) |
| B12 | EffectID_02 | 선택 — B11 과 동일 | |

- B8 에서 5 를 골랐고 B11·B12 도 모두 없음이면 확인한다.
  - "⚠ 아무 효과도 없는 버프다."
  - `1. 그대로` / `2. 효과 추가`
