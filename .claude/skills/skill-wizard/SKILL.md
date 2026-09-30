---
name: skill-wizard
description: 스킬 테이블(Shared/XLS/Skill.xlsx)에 신규 스킬을 번호 선택식 문답으로 만드는 마법사. 스킬 행과 SkillEffect, 필요하면 Buff·Projectile·Summon 하위 행까지 타입별 분기 질문으로 채우고, 요약·검증 후 사용자가 고르면 엑셀에 추가하고 ExcelDataTable 빌드까지 진행한다. "신규 스킬 추가", "새 스킬 만들기", "스킬테이블에 스킬 넣기", "/skill-wizard" 요청에 사용한다.
---

# 신규 스킬 마법사

한 번에 한 질문씩 묻고, 사용자는 번호나 값을 입력한다. 판별 컬럼(`CastingType`·`ScanType`·`EffectType`)에 따라 다음 질문이 달라진다. 쓰이지 않는 컬럼은 묻지 않는다.

- 질문 트리: [references/questions.md](references/questions.md)
- 요약·검증 규칙: [references/validation.md](references/validation.md)
- 엑셀 도구: `scripts/xlsx_tool.py` (inspect / append / build)

아래 명령의 `<SKILL_DIR>` 은 이 스킬이 로드될 때 표시되는 기본 디렉터리(Base directory)다. 저장소 루트에서 실행하면 `.claude/skills/skill-wizard` 와 같다.

## 진행 규칙

1. **한 메시지에 질문 하나.** 질문 외 설명은 1~2줄로 줄인다.
2. 질문 형식은 아래와 같다. 태그는 `[영역 번호]` 로 쓴다. 영역은 `Skill` / `Effect` / `Buff` / `Projectile` / `Summon` 이다.
   ```
   [Skill 7] 스킬의 발동방식(CastingType)을 선택해 주세요.
   1. Instant - 즉시 발동
   2. Casting - 시전 시간 경과 후 발동 (다음: 시전 시간)
   ...
   ```
3. **AskUserQuestion 도구는 쓰지 않는다.** 선택지가 4개로 제한되기 때문이다(CastingType 은 10개). 항상 평문 번호 목록을 쓴다.
4. 답변 해석
   - 선택 질문: 목록 번호로 받는다. 범위 밖이면 이유 한 줄과 함께 같은 질문을 다시 낸다.
   - 기존 ID 선택 질문: 번호 대신 ID 를 직접 입력해도 받는다.
   - 값 입력 질문: 앞뒤 공백을 제거한다. 숫자 칸은 숫자인지, 허용 범위 안인지 검사한다.
     - 확률·비율은 0~1 배율이다. 25 를 입력하면 "25% 는 0.25" 라고 안내하고 다시 묻는다.
5. **부가 필드**는 `1. 입력` / `2. 패스(비워둠 — 기본 동작 설명)` 를 먼저 묻는다.
   - 1 을 고르면 "값을 입력해 주세요." 로 값을 받는다.
   - 2 를 고르면 빈칸으로 두고 다음 질문으로 넘어간다.
6. **생략 조건**에 걸린 필드는 묻지 않고 빈칸으로 둔다. 생략 조건은 questions.md 에 있다. FALSE 인 bool 도 빈칸으로 둔다.
7. 선택지 설명은 0단계 inspect 결과(`#Enum` 시트 설명)에 questions.md 의 보충 설명을 붙인 것이다. 코드상 동작하지 않거나 위험한 값에는 `⚠` 를 붙이고, 권장값에는 `(권장)` 을 붙인다.
8. 사용자가 `취소` 를 입력하면 즉시 중단한다. 아무 파일도 쓰지 않는다.
9. **엑셀 추가와 빌드는 사용자가 해당 번호를 골랐을 때만 한다.**

## 0단계 — 준비

```bash
python "<SKILL_DIR>/scripts/xlsx_tool.py" inspect
```

- 출력 내용: enum 전체(설명 포함), 기존 ID(Skill / SkillEffect / Buff / Projectile / Summon / Stat / StatDetail), `#SkillParamDef`, 잠금 파일(`~$*.xlsx`) 여부.
- **선택지는 이 출력으로 만든다.** questions.md 의 enum 목록과 다르면 inspect 를 따른다.
  - 새로 생긴 값은 "(설명 없음)" 으로 보여 준다.
  - 새로 생긴 EffectType 은 `#SkillParamDef` 에 정의된 슬롯을 입력/패스 형식으로 묻는다.
- 잠금 파일이 있으면 "해당 xlsx 를 Excel 에서 닫아 달라" 고 먼저 알린다. 문답은 계속 진행해도 된다.
- 시작 인사는 한 줄로 끝내고 바로 `[Skill 1]` 질문을 낸다.

## 1~2단계 — 문답과 작업 스택

questions.md 의 A(Skill) 순서대로 묻는다. 효과나 하위 테이블을 새로 만들면 B~E 로 들어갔다가 돌아온다.

- **초안**: 지금까지의 답은 행 목록 `{file, sheet, values}` 으로 머릿속에 유지한다.
- **작업 스택**: "새로 생성" 을 고르면 다음 순서로 진행한다.
  1. 현재 질문 위치를 스택에 쌓는다.
  2. 한 줄로 알린다. 예: `▶ 새 SkillEffect 생성 — 끝나면 [Skill 19] 로 돌아갑니다`
  3. 하위 문답을 끝까지 진행한다.
  4. 만든 ID 를 원래 질문의 답으로 넣고 그 질문의 다음으로 복귀한다.
- **중첩 예**
  - 스킬 → 효과(Summon) → 소환물 → 소환물의 새 스킬 → 그 스킬의 효과
  - 소환물의 새 스킬은 소유자=소환물로 A 문답을 처음(`Skill 1`)부터 진행한다.
- **대기 목록**
  - 새로 만든 ID 는 즉시 대기 목록에 넣는다.
  - 이후 "기존 선택" 목록에 `(신규)` 표시로 함께 보여 준다.
  - ID 중복 검사와 번호 제안(NN)에도 대기 목록을 포함한다.

## 3단계 — 요약·검증

validation.md 대로 새 행 전체를 표로 보여 준다. 이어서 오류·경고와 획득 경로 안내를 보여 주고 아래 질문을 낸다.

```
[확인] 위 내용으로 진행할까요?
1. 엑셀에 추가
2. 항목 수정
3. 취소
```

- **오류가 1개 이상이면 1 을 막는다.** 선택지 1 을 `1. (오류 수정 전 추가 불가)` 로 표시한다.
- 수정은 다음 순서로 진행한다.
  1. 수정할 항목을 `행 ID.컬럼` 번호 목록으로 보여 준다.
  2. 고른 항목의 질문을 다시 묻는다.
  3. 그 답 때문에 분기가 바뀐 후속 질문도 다시 묻는다.
  4. 더 이상 쓰이지 않게 된 필드는 비운다.
  5. 요약으로 돌아온다.

## 4단계 — 엑셀 추가 (사용자가 1 을 고른 경우)

1. spec JSON 을 Write 도구로 만든다(UTF-8). 저장 위치는 세션 스크래치패드이고, 없으면 `%TEMP%` 에 `skill-wizard-spec.json` 으로 만든다.
   ```json
   {"rows": [
     {"file": "Skill.xlsx", "sheet": "#Skill", "values": {"ID": "Skill_X", "Name": "찌르기", "CastingParam": 3, "SkillVFXFacing": true}},
     {"file": "Skill.xlsx", "sheet": "#SkillEffect", "values": {"ID": "SE_X_Damage", "EffectParam_1": 2.5, "EffectParam_5": "Stat_Atk", "ChainEffectIDs": ["SE_Common_Stagger_Buff"]}},
     {"file": "Buff.xlsx", "sheet": "#Buff", "values": {"ID": "BUFF_X", "BlockFlags": ["Move", "Attack"]}}
   ]}
   ```
   - 빈칸은 키를 뺀다.
   - 숫자는 JSON 숫자로 쓴다. `EffectParam_*` 처럼 `!string` 칸이라도 숫자면 숫자로 쓴다.
   - bool 은 `true` 로 쓴다.
   - 배열 컬럼(`ChainEffectIDs`, `BlockFlags`)은 리스트로 쓴다.
   - 시트: `Skill.xlsx` 는 `#Skill` / `#SkillEffect`, `Buff.xlsx` 는 `#Buff`, `Projectile.xlsx` 는 `#Projectile`, `Summon.xlsx` 는 `#Summon`.
2. 드라이런으로 대상 행 번호를 보여 준다.
   ```bash
   python "<SKILL_DIR>/scripts/xlsx_tool.py" append "<spec 경로>" --dry-run
   ```
3. 바로 실제 추가를 실행한다. 사용자는 이미 1 을 골랐다.
   ```bash
   python "<SKILL_DIR>/scripts/xlsx_tool.py" append "<spec 경로>"
   ```
   - 도구는 전체를 먼저 검증한다. 하나라도 실패하면 **아무 파일도 쓰지 않는다.** 실패하면 메시지를 보여 주고 3단계 요약으로 돌아간다.
   - 새 행은 각 시트의 마지막 데이터 행 바로 다음에 붙는다. enum 정수값이 행 순서로 정해지므로 중간 삽입은 하지 않는다.
4. 빌드 여부를 묻는다.
   ```
   [빌드] 엑셀에 추가했습니다. ExcelDataTable 빌드를 진행할까요?
   1. 빌드
   2. 나중에 (빌드 전까지 게임은 옛 .bytes 를 읽는다)
   ```

## 5단계 — 빌드 (사용자가 1 을 고른 경우)

```bash
python "<SKILL_DIR>/scripts/xlsx_tool.py" build
```

- 이 체크아웃(워크트리 포함)의 `Shared/XLS` 를 읽어 이 체크아웃의 `Assets` 로 출력한다.
  - 도구는 변환기 exe 를 임시 폴더로 복사한 뒤 그 폴더에 경로 설정을 새로 만들어 실행한다.
  - 추적 중인 `Shared/Tools/ExcelDataTable/edt_local.json` 은 건드리지 않는다.
- 변환기는 **전체 xlsx 를 한 번에 변환**한다. 이번 스킬과 무관한 xlsx 변경이 있었다면 그것도 함께 반영된다.
- `exit code` 가 0 이 아니면 로그에서 에러 줄을 보여 주고 멈춘다. 이때 xlsx 는 이미 추가된 상태다.
- 성공하면 다음을 확인한다.
  - `ProjectOne/Assets/Project/Scripts/Core/ExcelData/edt_enums.cs` 를 Grep 해서 새 ID 가 전부 들어갔는지 확인한다.
  - `git status --short` 로 바뀐 파일을 보여 준다. 예상되는 파일은 xlsx, `edt_*.bytes`, `Table_*.cs`, `edt_enums.cs`, `edt_settings.json` 이다.

## 6단계 — 마무리 안내

다음을 짧게 안내한다.

- Unity 에디터로 돌아가면 리임포트된다. 워크트리에서 실행했다면 Unity 가 열고 있는 체크아웃에는 머지 전까지 반영되지 않는다.
- 플레이 후 부팅 로그의 Catalog 경고(`SkillParamCatalog` 등)를 확인한다. 빌드 통과가 데이터 정합을 보장하지 않는다.
- 획득 경로가 아직 등록되지 않았다면 validation.md 의 해당 목록을 보여 준다. 획득 경로 등록은 이 마법사의 범위 밖이다.
- 되돌리기: `git restore <xlsx 경로들>` 을 실행한 뒤 다시 빌드한다.
- 커밋은 사용자가 요청할 때만 한다.

## 코드로 확인된 주의사항 (설계서보다 우선)

- `OnDamaged` / `OnKill` 은 트리거를 호출하는 곳이 없어서 **현재 발동하지 않는다**.
- 코드에서 읽지 않는 컬럼이 있다. `Projectile.HitEffectID_2`, `Summon.ActionInterval`, `Force` 의 `Duration`(EffectParam_2) 이다. 그래서 묻지 않는다.
- `CastingType=Passive` 인 스킬은 AnimName·SkillVFX·SkillSFX·Cooldown 을 읽지 않는다. `Aura` 는 Cooldown 을 무시한다.
- `SkillCategory` 는 코드에서 Normal 인지 아닌지만 구분한다. 상시 적용 여부는 `CastingType=Passive` 가 결정한다.
- EffectOrigin `Attacker` / `Victim` 은 코드상 `Target` 과 똑같이 처리된다.
- `Location` 효과는 스킬 `ScanParam` 을 재탐색 반경으로 쓴다. ScanType 과 무관하다(Sector 120도 → 반경 120).
- 소모품·균열 스킬은 `SkillExecutor.Execute` 를 직접 호출하므로 EffectTime·Cooldown 이 무시된다.
- 설계서 `스킬시스템_설계.md` 1.3 과 달리 Projectile·Summon·Buff 는 `Skill.xlsx` 가 아니라 각자의 xlsx 에 있다.
