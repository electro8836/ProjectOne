# work.md — 히어로/몬스터 AI 작업 인계 노트

> 이 파일은 세션 간 작업 인계용이다. 다음 세션의 Claude는 이 파일을 먼저 읽고 "현재 상태"와 "남은 작업"부터 확인해 이어서 진행한다.
> 상세 설계 원본 플랜: `C:\Users\elect\.claude\plans\ai-piped-cook.md` (이 work.md와 중복되면 work.md의 "현재 상태"가 최신이다.)
> 마지막 갱신: 2026-06-05.

---

## 0. 작업 환경 / 규칙 (필수)

- Unity 6.4 / URP 2D. 유니티 프로젝트 루트: `E:\GameProjects\ProjectOne\ProjectOne`, 코드는 `Assets/Project/Scripts/` 아래.
- 네임스페이스 루트 `ProjectOne`. C# / 주석·문서·커밋 한국어.
- 컨벤션: **탭 들여쓰기, 비공개필드 `_`접두사, `[SerializeField]`, 조건문 항상 `{}`, `foreach` 금지(인덱스 for), 람다 지양(메서드 그룹), 비동기는 UniTask, 코루틴 대신 Tick 카운트다운.**
- Unity MCP 사용: 스크립트 수정 후 `refresh_unity`(compile=request) → `read_console`(errors)로 컴파일 0 확인. 플레이 검증은 `manage_editor play/stop` + `execute_code`.
  - 주의: `execute_code`에서 타입은 풀네임 사용(`ProjectOne.Unit.UnitBase` 등). Vector2/Vector3 혼용 시 캐스팅 명시.
  - 주의: 플레이모드가 가끔 자동 종료됨(execute_code 런타임 예외/에러일시정지 추정). 검증 중 `not playing` 나오면 재진입.

---

## 1. 무엇을 만들고 있나 (큰 그림)

히어로/몬스터 **AI 시스템**. 전체 범위:
- **히어로**(근접/원거리) 자동전투 — **[1단계 완료]**
- **몬스터**(일반/엘리트/던전) 접근 + 스킬 — [2단계 미착수]
- **레이드 보스**(체력 페이즈 100/75/50/25%, 코드 커스텀 스킬, 이동형/고정형) — [3단계 미착수]
- **PVP 적 히어로**(히어로 AI와 동일하나 고유스킬도 자동) — [3단계 미착수]

**채택 패러다임**: 경량 전략(behavior) 컴포넌트. `IAiBehavior` 구현체를 유닛별로 교체. (FSM/BT 풀구현 대신 단순화.)

---

## 2. 현재 상태 (2026-06-05) — 1단계 완료

### 히어로 자동전투 AI 최종 동작 (사용자 확정 사양)
- **이동·시선(facing)은 플레이어가 조작**(`HeroController` + `UnitMover`). AI는 이동/facing을 **건드리지 않는다**.
- **AI는 "스킬의 실제 공격범위 안에 적이 있으면 사용 가능한 스킬을 자동 시전"만** 담당.
  - 범위 판정은 거리 비교가 아니라 **스킬 실제 ScanType**(`TargetResolver.ScanByType`, `caster.Facing` 기준)으로 → Sector/Line 헛스윙 방지.
- **고유스킬(SpecialSkill_1/2)은 자동 제외**(HUD 버튼 수동). `castSpecial=false`. (PVP에서만 `true` 예정.)
- 검증됨: 히어로 제자리 유지(`moving=False`), `HeroController.enabled=True`, 범위 내 적 자동 시전·처치.

### ⚠️ 진화 이력 (되돌리지 말 것)
히어로 AI는 원래 "근접 드리블 추격 / 원거리 카이팅"으로 만들었다가, 사용자 요청으로 **이동 로직을 전면 폐기**하고 위 "스킬 자동 시전만" 모델로 바뀌었다. `MeleeBehavior`/`RangedBehavior`/`AiTargeting`/`AiState`는 **삭제됨**. 다시 만들지 말 것. (이동형 AI는 2·3단계 몬스터/보스용으로 새로 작성.)

---

## 3. 현재 코드 구조 (실제 파일)

### AI (`Assets/Project/Scripts/GamePlay/Unit/AI/`)
- `IAiBehavior.cs` — 단일 메서드 `void Tick(UnitBase self, Blackboard bb, float dt)`. 이동 유무는 behavior가 자체 결정.
- `AiBrain.cs` — POCO. `Tick(dt)`: `IsDead` 가드 → `behavior.Tick` 위임. `Blackboard` 보유(이동형 AI의 타겟/앵커 저장용, 히어로는 미사용).
- `Blackboard.cs` — POCO. `UnitBase Target`, `Vector2 Anchor` (현재 거의 미사용, 향후 이동 AI용).
- `AiBrainFactory.cs` — `CreateForHero(UnitBase)` → `HeroAutoBehavior` 주입(근접/원거리 구분 없음).
- `SkillSelector.cs` (정적) — `Select(UnitBase self, bool castSpecial)`. 보유 스킬 순회: 기본공격은 폴백 보류, Passive/OnHit·미충족 Special·쿨다운 스킵, **`HasEnemyInRange`(ScanByType+FilterByApplyTarget Enemy)** 충족 시 `TryCast`(첫 성공 반환). 전부 실패 시 기본공격 폴백.
- `Behavior/HeroAutoBehavior.cs` — `Tick`에서 `SkillSelector.Select(self, false)`만.

### 기존 파일 수정분 (1단계에서 건드린 것)
- `GamePlay/Unit/UnitBase.cs` — `protected AiBrain _brain;` + `SetBrain` + `LateUpdate`에서 `_skillContainer.Tick` 다음 `_brain?.Tick(deltaTime)`.
- `GamePlay/Unit/Factory/UnitFactory.cs` — 히어로 스킬 등록 재배선:
  - `Table_Character.Row`의 `BaseAttackSkill`/`PassiveSkill` → `Register(id, "Base")`, `SpecialSkill_1/2` → `Register(id, "Special")`.
  - `ComposeBase`(공통) / `ComposeHero`(히어로) / `ComposeUnit`(몬스터, 기존 `Table_SkillSet` 경로 유지) 분리.
  - `CreateHeroAsync(..., bool autoControl, ...)`: `autoControl`이면 `AiBrainFactory.CreateForHero` 주입. **HeroController는 끄지 않음**(플레이어 이동 유지).
- `GamePlay/Skill/SkillContainer.cs` — `bool IsSpecial(SkillInfo)`(Source=="Special"), `SkillInfo GetBasicAttack()`(IsBasicAttack 첫 항목) 추가.
- `GamePlay/Skill/TargetResolver.cs` — `IsEnemy(Faction,Faction)` private→**public**.
- `Test/MapTestSpawner.cs` — `HeroEntry.autoControl` 인스펙터 플래그 추가 + `CreateHeroAsync`에 전달.

### 유닛 겹침 방지 (별도 작업, 완료)
- `GamePlay/Unit/Movement/UnitMover.cs`:
  - `using ProjectOne.Unit;` 추가.
  - `ApplyMovement`의 `IsWalkable` 3곳 → `CanMoveTo(currentPos, nextPos)`(타일맵 통과 + 유닛 비겹침). 축분리 슬라이딩 유지.
  - 신규 `OverlapsNewUnit(currentPos, nextPos)`: `UnitContainer.Instance.All` 순회, self·죽은유닛 제외, **`min = (_unitRadius + u.Radius) * 0.5f`**(사용자가 반지름합의 절반으로 조정), 이미 겹친 유닛은 무시(끼임 탈출), 새로 겹치면 차단.
  - 콜라이더가 `IsTrigger`+Kinematic이라 물리충돌 없음 → 코드로 직접 분리(이동 차단 방식, 모든 유닛 대상).

---

## 4. 재사용해야 할 기존 시스템 (중요)

- **스킬 시전**: `unit.SkillContainer.TryCast(SkillInfo)` — 쿨다운/시전차단/CastingType/생존을 **내부에서 모두 판정**. AI는 "무엇을 시도할지"만 결정.
- **범위 스캔**: `TargetResolver.ScanByType(SkillScanType, p1, p2, caster)` + `FilterByApplyTarget(scanned, SkillApplyTarget.Enemy, caster)` (둘 다 정적, `GamePlay/Skill/TargetResolver.cs`). `SkillExecutor.ApplyEffects`와 동일 경로.
- **유닛 목록**: `UnitContainer.Instance.All`(전체) / `GetByType(UnitType)` (`GamePlay/Unit/UnitContainer.cs`). OnEnable/OnDisable 자동 등록.
- **적군 판정**: `TargetResolver.IsEnemy(self.Faction, other.Faction)` (이제 public).
- **이동**: `unit.Mover.Move(Vector2 dir, float speed)` / `Stop()` / `Facing`. 내부 배율 0.1, 타일맵+유닛 충돌은 `ApplyMovement`가 처리.
- **체력/페이즈용**: `unit.Vitals.Hp`(public float), 최대체력 `unit.Stats.GetStat(EDT.StatInfo.MaxHP)`. (Vitals에 비율 헬퍼는 없음 — 직접 나눗셈.)
- **플로우필드(몬스터 길찾기)**: `TilemapGrid.Instance` (`GamePlay/Map/TilemapGrid.cs`)에 **이미 구현됨** — `BakeFlowField(Vector2 worldPos)`, `GetFlowDirection(Vector2 worldPos)`. 중앙에서 히어로 위치로 주기적 1회 베이크하고 몬스터는 방향만 읽기.
- **몬스터 데이터**: `EDT.Table_Monster.Get(id)` — `MonsterType`(enum), `SkillSetID`, `BaseStatID`, `DropID[]`. 몬스터는 `MonsterPool.CreateItem`에서 `ComposeUnit` 호출.
- **스킬 데이터**: `EDT.Table_SkillInfo.Get(SkillInfo)` — `IsBasicAttack`, `CastingType`, `ScanType`, `ScanParam1/2`, `CooltimeSec` 등.

---

## 5. 남은 작업 (다음 세션 진행)

### 2단계: 몬스터 AI (일반/엘리트/던전) — 미착수
- 신규 `Behavior/MonsterApproachBehavior.cs`(`IAiBehavior`): `Tick`에서 (a) `TilemapGrid.GetFlowDirection`으로 히어로 접근 이동 (근거리는 직선, 장애물만 플로우필드) (b) `SkillSelector` 유사 로직으로 사용가능 스킬 시전.
  - 단, 히어로용 `SkillSelector`는 facing 기반인데 몬스터는 이동방향=facing이라 OK. 필요시 몬스터 전용 셀렉터.
- 플로우필드 **중앙 재베이크**: 신규 `MonsterAiCoordinator`(MonoSingleton) 또는 `MonsterSpawnManager`에서 히어로 위치로 0.3초 주기 1회 베이크. 몬스터 개별 베이크 금지.
- 몬스터 brain 주입: `MonsterPool.CreateItem`(또는 `Spawn`) 경로에서 `Table_Monster.MonsterType`로 behavior 결정해 `unit.SetBrain(...)`.
- 일반/엘리트는 같은 behavior + 스탯차로 흡수. 던전보스는 3단계 BossBehavior.
- **주의**: 몬스터가 이동하면 `UnitMover.OverlapsNewUnit`의 O(N) 순회가 몬스터마다 돌아 O(N²). 현재 규모(~30)는 OK지만 늘면 공간분할 고려.

### 3단계: 레이드 보스 + PVP — 미착수
- `GamePlay/Unit/AI/Boss/`:
  - `BossBehavior`(`IAiBehavior`): `bool _stationary`(고정형이면 이동 안 함), HP비율(`Vitals.Hp / Stats.GetStat(MaxHP)`)로 페이즈(100/75/50/25%) 판정 → 페이즈별 스킬 풀.
  - `BossPhase`(`[Serializable]`): `HpThreshold`, `SkillInfo[] Skills`, `SkillInfo[] EnterSkills`(진입 시 1회).
  - `IBossPattern` + `Patterns/Boss_*.cs`: 테이블엔 껍데기만 있고 코드로 구현하는 커스텀 스킬. 다단계 타이밍은 `SkillContainer.Schedule` 패턴과 동형의 자체 타이머(코루틴 금지).
- PVP: `HeroAutoBehavior` 재사용하되 **`castSpecial=true`**(고유스킬도 자동, 1번 우선). `AiBrainFactory`에 `CreateForPvp` 추가. 이동은 PVP AI가 해야 하므로 이동 로직 포함 behavior 필요(히어로처럼 플레이어 조작이 아님) — 2단계 몬스터 접근 로직 재사용 가능.

---

## 6. 검증 방법 (MCP 패턴)

테스트 씬: **`Dev_Battle`** (활성). `MapTestSpawner` 컴포넌트에 `_heroes`(HeroEntry: characterId/position/faction/**autoControl**)·`_monsters` 인스펙터 설정. autoControl=true면 AI 스킬 자동.

표준 절차:
1. `refresh_unity`(compile=request, scope=all) → `read_console`(types=[error]) 에러 0 확인.
2. `manage_editor play` → `Start-Sleep 4`(스폰 대기) → `execute_code`로 상태 덤프 → `manage_editor stop`.
3. 덤프 예: `UnitContainer.Instance.GetByType(UnitType.Hero/Monster)` 순회로 위치/HP/스킬 확인.

⚠️ **검증 함정**: `execute_code`로 유닛을 `transform.position` 텔레포트 + 강제 `Mover.Move` 하면 **비정상 고속/물리 꼬임** 발생(터널링, 위치 이상). 자연 스폰 상태에서 관찰하거나 무리 접근 방식으로 검증할 것.

---

## 7. 알려진 이슈 / 사용자 미결정

- **평타 넉백 밸런스**: 평타(예 `SKILL_BASEATTACK_01`)에 `DEBUFF_KNOCKBACK`(거리 0.5)이 효과로 붙어 사거리(0.5)와 같다 → 칠 때마다 적이 사거리 밖으로 밀림. 사용자는 이를 **의도된 기능**으로 확정(데이터는 `Shared/XLS/Skill.xlsx`). AI로 풀 문제 아님.
- **겹침 차단 터널링**: `OverlapsNewUnit`은 한 틱 이동이 `min` 거리보다 크면 건너뛸 수 있음. 정상 플레이어 속도(~1유닛/초, 틱당 0.02)는 무관. 강한 넉백 등 고속 이동에서 필요하면 스윕(분할) 검사 추가.
- **겹침 차단 거리**: 현재 `min = (myR + otherR) * 0.5`(사용자 조정). 더 붙이거나 떼려면 이 계수 조정.
- 캐릭터마다 평타가 다름(테스트 캐릭터는 `SKILL_BASEATTACK_03`, scan=Target 사거리2). Sector 평타 캐릭터는 facing 방향에 적 있어야 발동.

---

## 8. 핵심 파일 빠른 참조

| 역할 | 경로 |
|---|---|
| AI 두뇌 | `Assets/Project/Scripts/GamePlay/Unit/AI/AiBrain.cs` |
| 히어로 behavior | `.../AI/Behavior/HeroAutoBehavior.cs` |
| 스킬 셀렉터 | `.../AI/SkillSelector.cs` |
| behavior 팩토리 | `.../AI/AiBrainFactory.cs` |
| 유닛 베이스 | `.../GamePlay/Unit/UnitBase.cs` |
| 유닛 생성 | `.../GamePlay/Unit/Factory/UnitFactory.cs` |
| 이동/겹침 | `.../GamePlay/Unit/Movement/UnitMover.cs` |
| 범위 스캔 | `.../GamePlay/Skill/TargetResolver.cs` |
| 스킬 컨테이너 | `.../GamePlay/Skill/SkillContainer.cs` |
| 몬스터 풀 | `.../GamePlay/Unit/Factory/MonsterPool.cs` |
| 플로우필드 | `.../GamePlay/Map/TilemapGrid.cs` |
| 테스트 스포너 | `.../Test/MapTestSpawner.cs` |
