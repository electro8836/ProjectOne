# AWS 백엔드 연동 로드맵

> 로그인 및 유저데이터(계정/캐릭터/장비/스킬/재화)를 AWS에 저장하기 위한 작업 계획과 진행 상황.
> 세션이 바뀌어도 이 문서로 이어서 작업한다. **단계 완료 시 체크박스와 "진행 상황"을 갱신할 것.**

작성: 2026-06-18

---

## 확정된 방향

- **로그인**: 소셜 로그인(구글) — Cognito Identity Pool + Google IdP 연동
- **백엔드**: 서버리스 — Cognito + API Gateway + Lambda + DynamoDB
- **뽑기/랜덤보상은 서버 권위(server-authoritative)**: 스킬카드 뽑기, 던전 랜덤보상 같은 확률 로직은 클라이언트에서 굴리면 어뷰징되므로 **서버가 계산**해 결과 + 갱신된 데이터를 반환한다.
- **DB 스키마**: DynamoDB, PK `userId` + SK 도메인키. 도메인키는 현재 코드의 `ServerDataSystem.KeyCharacter / KeyInventory / KeySkill / KeyCurrency`와 그대로 맞춘다.

### 두 계층 분리 (중요)

| 계층 | 역할 | 대상 |
|------|------|------|
| **저장 계층** `IServerDataRepository` | 단순 read/write | 캐릭터·인벤토리·스킬·재화 등 클라가 바꿀 권한이 있는 데이터 |
| **액션 계층** `IServerCommand` (2단계 완료) | 서버가 결과를 계산해 반환 | `ClaimDungeonReward`(구현), `DrawSkillCard`(보류) 등 확률/보상 |

---

## 진행 상황

### ✅ 1단계: Repository 비동기화 (완료 — 2026-06-18)

저장 추상화를 네트워크 I/O 대비 비동기(UniTask)로 전환. 동작은 기존 로컬 JSON과 동일하며, 인터페이스 계약만 비동기로 바뀌어 서버 구현체를 끼울 준비 완료.

**변경된 파일**
- `Assets/Project/Scripts/Core/ServerData/IServerDataRepository.cs`
  - `TryLoad/Save` → `LoadAsync(key, ct) → (bool found, T data)` / `SaveAsync(key, data)` (UniTask)
- `Assets/Project/Scripts/Core/ServerData/LocalJsonRepository.cs` — 동기 I/O를 완료된 UniTask로 래핑
- `Assets/Project/Scripts/Core/ServerData/DevDataRepository.cs` — 메모리 접근을 완료된 UniTask로 래핑
- `Assets/Project/Scripts/Core/Flow/States/DataLoadState.cs` — `await LoadAsync<T>` 4건, `EnterAsync`를 async로
- `Assets/Project/Scripts/Contents/Inventory/Inventory.cs` — `save()` → `SaveAsync(...).Forget()`
- `Assets/Project/Scripts/Contents/Character/Loadout.cs` — 동일
- `Assets/Project/Scripts/Core/Currency/Wallet.cs` — 동일

**설계 결정**
- **Save는 fire-and-forget(`.Forget()`)**: 모델 변경 메서드를 async로 오염시키지 않기 위함. 저장 실패는 추후 서버 구현체 내부에서 로깅한다. (미관측 예외 주의)
- **Load는 `await`**: 데이터가 있어야 다음 흐름(Lobby)으로 진행 가능.

검증: Unity 강제 컴파일 → 콘솔 에러 0건.

---

### ✅ 2단계: 액션 계층(`IServerCommand`) 설계 (완료 — 2026-06-18)

저장 계층과 짝을 이루는 명령 계층 신설. 서버 권위 명령(현재 `ClaimDungeonReward`)의 계약을 확정하고, 끼울 수 있는 로컬 구현을 마련했다.

**추가된 파일** (`Assets/Project/Scripts/Core/ServerCommand/`)
- `IServerCommand.cs` — `UniTask<TResponse> ExecuteAsync<TRequest, TResponse>(string action, TRequest request, CancellationToken ct)`. `action`은 서버 라우트/람다 식별자, request/response는 JSON 직렬화 DTO.
- `ServerCommandSystem.cs` — 진입점(`Command` 프로퍼티 + `SetCommand`). 액션 상수 `ActionClaimDungeonReward`. (`ServerDataSystem`의 명령 계층 버전)
- `ClaimDungeonRewardDto.cs` — `RewardGrant`(결과 항목) + `ClaimDungeonRewardRequest`{ mapId, characterId } + `ClaimDungeonRewardResponse`{ grants, inventory?, character?, currency? }
- `LocalServerCommand.cs` — 기본 구현체. `mapId`로 `Table_MapInfo.ClearRewardIDs`를 조회해 확률 계산(`RewardService` 위임).

**변경된 파일**
- `Assets/Project/Scripts/Contents/Reward/RewardService.cs` — `GrantClearRewards`가 `List<RewardGrant>` 반환(지급 내역). 기존 호출자(`BattleDirector`)는 반환값 무시 → 동작 무변경.

**설계 결정**
- **요청은 식별자만**: 클라는 `mapId`만 보내고 보상ID/확률은 **서버가 `Table_MapInfo`로 조회·계산**(클라가 보상ID를 정하지 못하게 → 어뷰징 차단).
- **응답 = 결과 + 갱신 데이터**: `grants`(연출용) + 도메인 DTO(`inventory`/`character`/`currency`). 도메인 DTO는 **nullable** — AWS 구현은 갱신본을 채워 반환하고 호출자가 `Account.Set*`으로 반영, **로컬 구현은 `Account`를 직접 변경**하므로 비워둔다(null). 한 계약으로 양쪽 동작.
- **`DrawSkillCard`는 보류**: `SkillData`/`SkillBook`이 빈 상태라 지금 DTO 설계는 추측이 됨. 스킬 시스템 구현 후 동일 패턴(`ActionDrawSkillCard` + DTO + `LocalServerCommand` 분기)으로 추가한다.
- **확률 테이블 위치**: 보상 확률은 `Table_*`(Excel→.bytes)에 있고 로컬 구현이 직접 읽는다. AWS 단계에서 동일 테이블을 Lambda가 읽거나 DynamoDB로 옮겨 **클라 배포 없이 밸런싱** 가능하게 한다(3단계에서 확정).

검증: Unity 강제 리프레시 + 컴파일 → 콘솔 에러 0건.

> **5단계 결선 시 할 일**: `BattleDirector`가 `RewardService.GrantClearRewards` 직접 호출 대신 `ServerCommandSystem.Command.ExecuteAsync<ClaimDungeonRewardRequest, ClaimDungeonRewardResponse>(...)`를 거치도록 변경. 응답의 도메인 DTO가 null이 아니면 `Account.Set*`로 반영.

### 🔶 3단계: AWS 인프라 구축 (진행 중 — 가이드 작성 완료, 사용자가 콘솔 작업 중)

> **따라하기 가이드: [`docs/aws-infra-setup.html`](aws-infra-setup.html)** — 콘솔 단계별 + Lambda 코드 + IAM 정책 + 테스트 curl 포함. 사용자가 이 문서를 보며 직접 AWS 콘솔에서 진행 중.

**인증 방식 변경 (중요)**: 원래 "Cognito Identity Pool"로 적었으나 **Cognito User Pool + Google 페더레이션**으로 변경한다.
- JWT 발급 → API Gateway HTTP API의 **JWT Authorizer**가 검증하는 흐름엔 User Pool이 정석. Lambda는 JWT의 `sub`를 `userId`로 사용.
- Identity Pool은 클라가 DynamoDB에 직접 접근할 때만 필요 → API Gateway 경유라 불필요.

**구성 (가이드 기준)**
1. Google OAuth 클라이언트 생성 + Cognito User Pool에 Google IdP 연동 (앱 클라이언트 + 호스팅 도메인)
2. DynamoDB `ProjectOneUserData` (PK `userId`=JWT sub, SK `domainKey`, 속성 `data`=도메인 DTO JSON 문자열)
3. IAM 실행 역할 `ProjectOne-Lambda-Role` (DynamoDB 최소 권한)
4. Lambda: `loadUserData`(GET) / `saveUserData`(PUT) — **완성**, `claimDungeonReward`(POST) — **스켈레톤**
5. API Gateway HTTP API + JWT Authorizer + 라우트 3개

**엔드포인트 ↔ 코드 매핑**
- `LoadAsync(key)` ↔ `GET /userdata/{key}` / `SaveAsync(key,data)` ↔ `PUT /userdata/{key}`
- `IServerCommand.ExecuteAsync(ActionClaimDungeonReward, …)` ↔ `POST /commands/claimDungeonReward`

**🔴 미결정 — 보상 테이블 위치**: `claimDungeonReward`가 확률을 계산하려면 `Table_MapInfo`/`Table_Reward` 데이터가 서버에 필요(현재 클라 `.bytes`에만 존재). 정해야 Lambda 계산부 구현 가능.
- (A) Lambda 동봉 JSON — 단순, 밸런싱 시 재배포
- (B) DynamoDB 설정 테이블(`ProjectOneConfig`) — **클라 배포 없이 밸런싱 (권장)**

### ⬜ 4단계: 인증 플로우 (Unity)

- 구글 로그인 → Cognito JWT 획득/보관/갱신
- 로그인 씬/UI
- AWS SDK for .NET 또는 직접 HTTP 호출 방식 결정 → 패키지 도입

### ⬜ 5단계: 서버 Repository 구현 + 결선

- `AwsServerRepository : IServerDataRepository` 구현 (API Gateway 호출)
- 부트 시점 `ServerDataSystem.SetRepository(new AwsServerRepository(...))`
- 액션 계층 클라이언트 구현체 연결

### ⬜ 6단계: 신규 계정 최초 데이터 생성

- 첫 로그인 시 서버에 기본 데이터 생성/초기화 처리

---

## 참고 (현재 코드 구조)

- 유저데이터 단일 진입점: `Account`(`ProjectOne.UserData`, `Core/Account/Account.cs`) — `Inventory`/`Loadout`/`SkillBook`/`Wallet` 소유, 도메인별 `Set*`로 주입
- 저장 진입점: `ServerDataSystem.Repository` (`Core/ServerData/`) — 이 한 곳의 구현체만 교체하면 서버 연동
- 로드 통제: `DataLoadState`(`Core/Flow/States/`)가 Repository에서 받아 Account에 주입
- 런타임 모델(`UserData`)과 직렬화 DTO(`ServerData`) 계층 분리
