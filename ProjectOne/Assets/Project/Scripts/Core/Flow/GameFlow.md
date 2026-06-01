# 게임 흐름 관리 시스템 (GameFlow State Machine) — 작업 계획

> 이 문서는 구현 전 작업 계획서다. 코드는 아직 작성하지 않았으며, 이 문서를 기준으로 이어서 작업한다.

## 배경 (왜 만드는가)

현재 `GameBootstrapper`(부트 씬 배치 MonoBehaviour)가 테이블·SFX 로드만 하고 끝나며, 이후 게임 흐름(씬 전환, 단계 진행)을 관장하는 객체가 없다. 씬 전환이 흩어져 있어 "씬이 게임을 끌고 다니는" 구조가 되기 쉽다.

이를 역전하기 위해, **게임의 진행을 상태(State)의 전이로만 표현**하는 순수 C# 싱글톤 `GameFlow`를 도입한다. 상태마다 진입/이탈 시 할 일(씬 로드, 시스템 on/off)을 캡슐화하고, 부트스트래퍼는 "흐름을 켜는 스위치"로 축소한다.

### 합의된 설계 결정
- **클래스 기반 상태 패턴** — 상태마다 클래스, `EnterAsync`/`ExitAsync` 분리
- **UniTask 비동기 전이** — 씬 로드·다운로드 등을 `await`
- **`Update()` 없음** — 흐름 상태의 책임은 "전이"지 매 프레임 로직이 아님. 매 프레임 갱신이 필요한 시스템은 해당 상태가 켜는 별도 MonoBehaviour가 자기 `Update`에서 처리
- **씬 로딩 주도 + 상태 변경 이벤트 발행** — 각 상태가 씬을 로드하고, 전이 완료 시 `EventManager`로 `GameStateChangedEvent` 발행
- **상태 ≠ 씬 (1:1 아님)** — Patch/Login/DataLoad는 별도 씬 없이 Title 씬 위 오버레이로 동작

---

## 상태 시퀀스

```
GameBootstrapper.Start()
      ▼
[BootState]      (Boot 씬)    매니저 초기화 + 로컬 설정 로드 (가벼운 것만)
      ▼  Title 씬 로드
[TitleState]     (Title 씬)   로고 표시, "터치하여 시작"
      ▼  (터치)               이후 단계는 Title 씬 유지 + 오버레이
[PatchState]     (Title 위)   Addressables 카탈로그 갱신 + 번들 다운로드 → 정적 테이블 로드
      ▼
[LoginState]     (Title 위)   서버 인증/로그인
      ▼
[DataLoadState]  (Title 위)   유저 데이터 로드
      ▼
[StageState]     (Stage 씬)   메인 전투/진행
      ▼  ⇅ 입장/복귀
[DungeonState]   (Dungeon 씬) 던전 콘텐츠
```

### 부트 작업 배치 근거 (의존성 순서)
- **에셋번들 다운로드** → `PatchState` (모든 로드보다 먼저, 무거운 진행률/재시도 UI라 독립)
- **정적 테이블 로드** (`TableLoader`) → `PatchState` 후반 (번들 다운로드 후에만 가능, 서버 불필요)
- **로그인** → `LoginState` (네트워크 의존, 유저 데이터의 선행 조건)
- **유저 데이터 로드** → `DataLoadState` (로그인 성공 후에만 가능)

---

## 구현 범위

이번 작업은 **골격 + 씬 4개 + 실제 동작하는 Boot/Title 전이**까지. 서버/네트워크 백엔드가 아직 없으므로 `PatchState`/`LoginState`/`DataLoadState`는 **스텁(즉시 다음 상태로 전이 + TODO 주석)**으로 둔다. 실제 다운로드/로그인 로직은 백엔드 준비 후 각 상태의 `EnterAsync` 안만 채우면 된다.

| 상태 | 구현 수준 |
|------|----------|
| `GameFlow`, `IGameState`, `GameStateChangedEvent` | 완전 구현 |
| `BootState` | 기존 `GameBootstrapper` 로드 시퀀스 이전 (실제 동작) |
| `TitleState` | 씬 로드 + 입력 대기 → 다음 전이 (실제 동작) |
| `PatchState` / `LoginState` / `DataLoadState` | 스텁 (즉시 다음 상태, TODO 주석) |
| `StageState` / `DungeonState` | 씬 로드 + 전이 골격 (전투 로직은 범위 외) |

---

## 변경/신설 파일

### 신설 — `Scripts/Core/Flow/`
기존 `Core/Event/`, `Core/Audio/`와 동일한 패턴으로 새 하위 폴더를 둔다.

- **`Core/Flow/IGameState.cs`** — 상태 인터페이스
  ```csharp
  public interface IGameState
  {
      UniTask EnterAsync(CancellationToken ct);
      UniTask ExitAsync();
  }
  ```
- **`Core/Flow/GameFlow.cs`** — 순수 C# 싱글톤. `Singleton<GameFlow>` 상속, `protected GameFlow()`. `ChangeStateAsync(IGameState)` 제공:
  1. 진행 중 전이를 `CancellationTokenSource`로 취소 후 새 토큰 발급
  2. `await _current.ExitAsync()` (있으면)
  3. `_current = next; await next.EnterAsync(ct);`
  4. `EventManager.Instance.Publish(new GameStateChangedEvent(...))`
  - `CurrentState` 읽기 프로퍼티 노출
- **`Core/Flow/States/`** — `BootState.cs`, `TitleState.cs`, `PatchState.cs`, `LoginState.cs`, `DataLoadState.cs`, `StageState.cs`, `DungeonState.cs`

### 수정
- **`Core/Event/Events.cs`** — `GameStateChangedEvent` 추가 (`readonly struct`, `System.Type StateType` 또는 상태 이름 보유). 기존 이벤트와 동일 스타일.
- **`Boot/GameBootstrapper.cs`** — `bootAsync` 내용을 `BootState.EnterAsync`로 이전하고, `Start()`는 `GameFlow.Instance.ChangeStateAsync(new BootState()).Forget()` 한 줄로 축소.
  - `.Forget()` 미관측 예외 주의 — 종료 크래시 방지 위해 `BootState` 내부 try/catch로 예외 처리

### 신설 씬 — `Assets/Project/Scenes/`
- 기존: `1.Bootstrap.unity`, `1.Title.unity` 존재
- 신설: **`Stage.unity`**, **`Dungeon.unity`** (각 씬에 Camera + Directional Light 포함, MCP `manage_scene`로 생성)
- Build Settings에 4개 씬 등록

---

## 재사용할 기존 자산
- **`Singleton<T>`** (`Utils/Singleton/Singleton.cs`) — `GameFlow`가 상속 (EventManager와 동일 패턴)
- **`EventManager`** (`Core/Event/EventManager.cs`) — 상태 변경 브로드캐스트
- **`TableLoader.LoadAllAsync` / `AudioManager.PreloadSFXByLabelAsync`** — `BootState`로 이전
- **UniTask** — 비동기 전이. 씬 로드는 `SceneManager.LoadSceneAsync(...).ToUniTask(cancellationToken: ct)`

---

## 코딩 규칙 (CLAUDE.md 준수)
- 비공개 필드 `_` 접두사 (`_current`, `_transitionCts`, `_isTransitioning`)
- 콜백은 메서드 그룹 (람다 금지), 조건문 단일 라인도 `{}`
- 들여쓰기 탭, 한국어 주석
- 제어 블록 닫는 `}` 뒤 코드 이어지면 빈 줄 1줄
- `foreach` 금지 — 인덱스 `for`

---

## 검증 (end-to-end)

1. **컴파일** → 신설/수정 후 MCP `read_console`로 컴파일 에러 0 확인 (`editor_state.isCompiling` 폴링)
2. **전이 동작** → Bootstrap 씬에서 플레이모드 진입. 로그로 흐름 확인:
   - `BootState`: 기존 "부트 완료 — Character:.. Monster:.." 로그 출력
   - 자동으로 `TitleState` 진입 → Title 씬 로드 확인
   - 각 전이마다 `GameStateChangedEvent` 발행 → 임시 구독 로그로 상태명 출력 확인
3. **전이 직렬화** → 전이 도중 다른 전이 호출 시 `CancellationToken`으로 이전 전이가 취소되는지 (스텁 상태에서 강제 호출로 확인)
4. **부트스트래퍼 축소 확인** → `GameBootstrapper`가 `ChangeStateAsync(new BootState())` 한 줄만 호출하는지

> Stage/Dungeon 전이는 트리거(버튼/이벤트)가 아직 없으므로, 임시로 디버그 키 입력이나 인스펙터 버튼으로 `ChangeStateAsync` 호출해 씬 로드만 확인.
