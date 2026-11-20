# UI 구조 설계 (UI Toolkit)

> 2D 탑다운 방치형 핵앤슬래시 (Unity 6.4, 세로 1080 x 1920)
> 관련 문서: `무기마스터리시스템_설계.md`, `아이템시스템_설계.md`
> **이 문서는 UI 전체를 UI Toolkit 으로 옮긴 뒤의 목표 구조와 전환 순서를 정의한다.** 월드 공간 UI(머리 위 게이지·피해 숫자)는 범위 밖이다.

---

## 1. 개요

### 1.1 원칙

- **그리는 층만 바꾼다.** 창 스택·Addressables 참조카운트·MVP(Presenter + IView)는 그대로 쓰고, View 만 UI Toolkit 으로 교체한다.
- **단계마다 게임이 돌아간다.** 전환 중에는 UGUI 화면과 UI Toolkit 화면이 같은 창 스택에 함께 올라간다.
- **화면 수만큼 패널을 만들지 않는다.** 층(Layer)마다 UIDocument 하나, 화면은 그 루트에 붙였다 뗀다.

### 1.2 유지하는 것

| 대상 | 역할 |
|---|---|
| `UIManager` (MonoSingleton) | 층 관리, 창 스택, 팝업 스택, 뒤로가기, 네트워크 딤 |
| `UIScreen` | 모든 화면의 수명 주기(`OnOpenAsync` / `OnCloseAsync` / `HidesNavigationBar`) |
| `UIScreenCatalog` | 화면 ID → Addressable 주소 |
| `Presenter<TView>` / `IView` | Passive View MVP. Presenter 가 결정하고 View 는 그리기와 입력 이벤트만 |

---

## 2. 층(Layer) 구성

### 2.1 층 목록

`UIManager` 아래에 층마다 UIDocument 하나를 둔다. UI Toolkit 패널과 UGUI Canvas(Screen Space Overlay)는 정렬 값대로 섞여 그려지므로, 남은 UGUI 와의 위아래도 정렬 값으로 정한다.

| 층 | PanelSettings | 정렬 | 내용 |
|---|---|---|---|
| (UGUI) 조이스틱 | `UIPrefab_Joystick` 의 Canvas | 80 | 이동 조이스틱 |
| HUD | `PanelSettings_HUD` | 90 | 메인 HUD |
| (UGUI) HUD 캔버스 | `UIManager._hudCanvas` | 100 | 던전 HUD, 월드 보스 게이지 |
| Window | `PanelSettings_Window` | 200 | 전체 창(장비·마스터리·상점 …) |
| Navigation | `PanelSettings_Navigation` | 300 | 하단 네비게이션 바 |
| Popup | `PanelSettings_Popup` | 350 | 팝업, 툴팁 |
| System | `PanelSettings_System` | 400 | 알림 메시지, 획득 로그, 네트워크 딤 |
| Loading | `PanelSettings_Loading` | 999 | 로딩 화면 — 층 구조의 유일한 예외 |
| World | (UGUI WorldSpace Canvas 유지) | — | 머리 위 게이지, 피해 숫자 |

- 메인 HUD 는 조이스틱 위·던전 HUD 아래에 놓인다. HUD 의 빈 곳은 `pickingMode = Ignore` 라 터치가 조이스틱까지 내려간다.
- 로딩 화면(`LoadingUI`)은 `LoadingManager` 가 UIManager 와 따로 띄우고 모든 UI 를 덮어야 해서, 층에 붙지 않고 자기 UIDocument 를 가진다.
- 모든 PanelSettings 는 기준 해상도 1080 x 1920 · Match Height(1) 이고 테마 `Theme.tss`(기본 테마 + `Theme.uss`)를 쓴다. 층 루트에 바로 붙는 요소(툴팁·네트워크 딤)도 테마를 받는다. 각 UXML 도 `<Style>` 로 `Theme.uss` 를 가져온다.
- 층 루트는 `pickingMode = Ignore` 로 두어 빈 곳의 입력을 아래 층으로 통과시킨다. 자식 UIDocument 는 `UIManager.Awake` 시점에 루트가 없으므로 층을 처음 쓸 때 설정한다.
- 안전 영역(노치)은 층 루트의 padding 으로 한 번만 처리한다. `Screen.safeArea` 가 바뀔 때만 다시 계산한다.

### 2.2 입력

- UGUI 와 UI Toolkit 이 함께 있는 동안 EventSystem 하나가 둘 다 처리한다.
- 클릭음은 층 루트에 올라오는(bubble) `ClickEvent` 하나를 걸어 일괄로 낸다. 버튼마다 연결하지 않는다.
  - 대상: `Button`, 탭(`.tab` · `.nav-tab` — 탭 소리), 버튼이 아닌데 누르는 요소(`.sfx-click` — 아이템 칸·목록 행 등. 클릭 콜백을 걸 때 이 클래스도 붙인다).
  - 소리는 UGUI 버튼 테마와 같은 FMOD 이벤트다(`UIManager._clickSfx` / `_tabClickSfx`).
  - 끌어서 스크롤한 뒤의 클릭은 `ScrollDragManipulator` 가 삼키므로 소리가 나지 않는다.
- 모바일 게임이라 hover 표시를 쓰지 않는다. `Theme.uss` 가 기본 테마의 버튼 hover·눌림 배경을 같은 강도로 덮고, 색 배경 버튼은 화면 USS 에서 `:hover:enabled` 로 자기 색을 다시 지정한다.
- 뒤로가기 키는 팝업 스택 → 창 스택 순으로 맨 위부터 닫는다.

---

## 3. 화면(창)

### 3.1 ToolkitScreen

```csharp
// UI Toolkit 화면의 베이스. UIDocument 를 갖지 않고, UIManager 가 넘겨준 층 루트에 붙는다.
public abstract class ToolkitScreen : UIScreen
{
	[SerializeField] private VisualTreeAsset _layout;

	protected bool IsBound { get; }			// OnBind 가 끝났는가 (Update·OnDestroy 에서 거른다)
	protected VisualElement Root { get; }

	public void Attach(VisualElement layerRoot);	// 처음이면 _layout 을 생성해 OnBind 호출, 층 루트에 붙인다
	public void Detach();							// 층 루트에서 뗀다 (OnDestroy 가 부른다)
	protected void PlayPopupOpen();				// .popup 표시 + 한 프레임 뒤 .popup--open

	// Q()·입력 연결·Presenter 연결은 여기서 한 번만 한다
	protected abstract void OnBind(VisualElement root);
}
```

- 화면은 계속 프리펩(Addressable)이다. 캐릭터 미리보기처럼 MonoBehaviour 부품이 필요한 화면이 있기 때문이다.
- `UIManager.OpenWindowAsync` 는 프리펩을 생성한 뒤, `ToolkitScreen` 이면 Window 층 루트에 `Attach` 한다. 닫을 때는 파괴(→ `Detach`)·Release 한다. 창 스택 로직은 UGUI 화면과 공유한다.
- 상시 UI 도 같은 방식이다: 메인 HUD → HUD 층, 네비게이션 바 → Navigation 층, 알림 메시지·획득 로그 → System 층.
- View 는 `ToolkitScreen` 을 상속하고 화면의 `IView` 를 구현한다. Presenter 는 UGUI 판과 같은 것을 쓴다.

### 3.2 View 규칙

- View 는 표시 메서드와 입력 이벤트(`event Action<...>`)만 노출한다. 판단은 Presenter 가 한다.
- 콜백은 메서드 그룹으로 연결한다(람다 금지). 요소별 식별값은 `userData` 에 붙인다.
- 상태 표시는 USS 클래스 켜고 끄기(`EnableInClassList`)로 한다. 인라인 스타일은 너비 비율·배경 이미지처럼 값이 데이터인 경우에만 쓴다.

---

## 4. 팝업

### 4.1 공통 진입점

팝업은 두 경로로 연다.

- **결과형 공용 팝업** — `ShowPopupAsync` 하나로 연다. 공용 확인 팝업(`UIPopupId.Common`)이 쓴다.
- **화면별 팝업** — 인자·결과 모양이 팝업마다 달라 `UIManager.ShowXxxPopupAsync` 를 둔다. 프리펩 생성은 `spawnPopup` 이 하고 `ToolkitScreen` 이면 Popup 층에 붙인다. 같은 팝업이 겹쳐 열리지 않도록 팝업별 취소 토큰을 하나씩 둔다.

```csharp
public enum UIPopupId { None, Common }

// 결과를 await 로 받는다. 닫힘·취소 시 Release 까지 처리한다.
UniTask<TResult> ShowPopupAsync<TPopup, TArgs, TResult>(UIPopupId id, TArgs args, CancellationToken ct)
	where TPopup : UIPopup<TArgs, TResult>;
```

```csharp
public abstract class UIPopup<TArgs, TResult> : ToolkitScreen
{
	public UniTask<TResult> ShowAsync(TArgs args, CancellationToken ct);
	protected abstract void OnShow(TArgs args);
	protected virtual TResult CancelledResult { get; }	// 취소·닫기로 끝났을 때의 결과
	protected void Close(TResult result);				// 내부 UniTaskCompletionSource 완료
}
```

- `UIPopupCatalog` 가 `UIPopupId` → 주소를 소유한다(`UIScreenCatalog` 와 같은 방식).
- 결과가 없는 팝업은 `TResult` 로 빈 구조체 `NoResult` 를 쓴다.
- 팝업은 Popup 층 루트에 붙는다. 서로 다른 팝업은 겹쳐 열 수 있다.
- 타이틀 복귀(`ResetForTitle`)는 떠 있는 팝업을 모두 취소한다.

### 4.2 열고 닫는 연출

- 레이아웃 최상위에 `.popup`(딤 + 창)을 둔다. 틀 스타일(`.popup` · `.popup__dim` · `.popup__window` · `.popup__head` · `.popup__buttons`)은 `Theme.uss` 에 있다.
- 열림은 `UIPopup` 이 `.popup` 을 표시한 뒤 한 프레임 뒤에 `.popup--open` 을 붙여 USS 전환을 건다(같은 프레임에 붙이면 전환이 생략된다).
- 닫힘은 결과 확정 즉시 파괴한다.

---

## 5. 재사용 컴포넌트

컴포넌트는 MonoBehaviour 도 UXML 템플릿도 아닌 **일반 C# 클래스**다. 부모 요소를 받아 자기 요소를 코드로 만들거나 레이아웃에서 찾고, 화면은 `Bind…` 만 호출한다. 모양은 USS 클래스가 정한다.

| 컴포넌트 | 내용 | 쓰는 곳 |
|---|---|---|
| `ItemCellView` | 등급 틀·배경·아이콘·분류 배지·레벨·개수·품질 막대·잠금·장착 표시 | 장비·보관함·보상·상점·분해·우편 첨부 |
| `CostView` | 재화 아이콘 + 수량(부족하면 빨강) | 강화·제작·상점 비용 |
| `HoldRepeatManipulator` | 누르고 있으면 반복 클릭 | 수량 증감 버튼 |
| `ScrollDragManipulator` | 드래그 스크롤 + 드래그 뒤 클릭 막기 | 스크롤 목록 |
| `…Cell` / `…Row` | 화면 전용 목록 칸(상점 상품·코스튬·펫·우편·랭킹·퀘스트·출석·패스) | 각 화면 |
| `Hud…` | 메인 HUD 위젯(재화·히어로 정보·맵·퀘스트·버프·보스 배너·궁극기) | 메인 HUD |

- 아이콘은 `ToolkitIconLoader` 로 Addressable 스프라이트를 개별 로드하고, 화면이 닫힐 때 일괄 Release 한다. 화면 하나가 로더 하나를 갖고 컴포넌트에 넘긴다.
- 컴포넌트 안에서 매 프레임 갱신하지 않는다. 값이 바뀔 때만 다시 그린다.
- 자기 이벤트를 직접 받는 HUD 위젯(`Hud…`)은 화면(`MainHud`)이 만들고 정리(`Dispose`)한다. 폴링이 필요한 것만 화면의 `Update` 가 `Tick` 을 불러 준다.

---

## 6. 스타일

### 6.1 테마

- 모든 UXML 이 `Common/Theme.uss` 를 `<Style>` 로 가져온다. `Theme.uss` 는 색 변수, 글꼴, 도트 부품(9-slice), 버튼·칩·게이지·팝업 틀 공통 클래스를 가진다.
- 도트 부품은 `UIToolkit/Pixel/` 에 있고 `Tools/pixelui/generate_pixel_ui.py` 가 생성한다. 도트 1칸 = 4px(`-unity-slice-scale: 4px`).
- 화면별 USS 는 그 화면 배치만 다룬다. 색·글꼴을 화면 USS 에서 새로 정하지 않는다.

### 6.2 글꼴

- 갈무리11 Bold 하나만 쓴다. 영어·숫자·기호도 같은 글꼴(도트)로 나온다.
- 글자 크기는 12 의 배수(36·48·60·72)만 쓴다. 최소 36px.
- 외곽선은 획을 안쪽으로 깎으므로 얇게 쓴다: 36px → 1px, 48px → 2px, 72px → 4px.
- 레벨·수치 강조는 배경 칩 없이 숫자만 크게 금색, 단위·접두어(`Lv.`, `/ 최대`)는 작은 기본색으로 쓴다.

---

## 7. 목록

| 경우 | 방식 |
|---|---|
| 개수가 적고 고정(장착 칸, 스킬 칸) | UXML 에 미리 배치 |
| 수십 개 이하(스킬 목록, 무기별 마스터리) | 요소를 만들어 두고 재사용(풀) |
| 많아질 수 있음(가방, 우편, 랭킹) | `ListView` 가상화. 격자는 한 줄에 N 칸씩 묶은 행을 항목으로 쓴다 |

- 스크롤은 `ScrollDragManipulator` 로 드래그 스크롤을 붙인다. 모든 ScrollView 가 이것을 쓴다.
  - 내용이 보이는 영역보다 작으면 움직이지 않는다. 마우스 끌기는 스크롤 범위 안으로만 옮기고, 터치는 누를 때마다 Elastic(넘치면 튕김) / Clamped 를 고른다.
  - 범위는 `verticalScroller` 의 값으로 판단한다(줄바꿈 격자는 내용 컨테이너 높이가 실제 내용과 다르다).
- 스크롤 영역을 감싼 틀의 안쪽 여백은 틀 테두리보다 두껍게 둔다(도트 석판 `px-paper` 는 26px 이상) — 얇으면 스크롤 중인 칸이 테두리 위에 비친다.

---

## 8. 성능 규칙

- `Q()` 는 `OnBind` 에서 한 번만 한다.
- 글자·너비는 값이 바뀔 때만 쓴다. HUD 처럼 자주 갱신되는 값은 이전 값을 들고 비교한다.
- 매 프레임 스타일을 바꾸지 않는다. 애니메이션(접기·사라짐·팝업 열림)은 USS 전환을 쓰고, 코드는 클래스만 붙이고 뗀다.
- HUD 의 폴링 위젯(버프·궁극기)은 0.1초 간격으로 읽고, 남은 시간 글자는 정수 초가 바뀔 때만 쓴다.
- 프로파일러로 HUD 의 UI Toolkit 갱신 비용(Layout·Repaint)을 확인한다.

---

## 9. 폴더 구조

```
Assets/Project/UIToolkit/
  Common/       Theme.tss · Theme.uss · PanelSettings_* (층 5종 + Loading)
  Components/   ItemCell.uss
  Pixel/        도트 9-slice 부품 (pixelui 생성)
  Equipment/ · Mastery/   장비·마스터리 화면
  Screens/      Shop/ · World/ · Craft/ · Costume/ · Pet/
  Popups/       Popups.uss(공통) · Common/ · Reward/ · ItemInfo/ · Mail/ · Ranking/ · Quest/ …
  HUD/          MainHud/ · Navigation/ · Notice/ · SystemLog/ · Loading/

Assets/Project/Scripts/UI/
  Core/         UIManager · UIScreen · UIScreenCatalog · UIPopupCatalog · LoadingUI · MVP/
  Toolkit/      ToolkitScreen · UIPopup · ToolkitIconLoader · ScrollDragManipulator · Components/ · Editor/
  Screen/ · Popup/ · HUD/ · Notice/   화면별 View 와 Presenter (HUD/Toolkit/ 에 Hud… 위젯)
```

---

## 10. UGUI 로 남기는 것

| 대상 | 이유 |
|---|---|
| 머리 위 게이지·피해 숫자 (World Canvas) | 월드 좌표에 붙어 유닛을 따라간다 |
| 조이스틱 (`UIPrefab_Joystick`) | EventSystem 포인터 드래그로 동작한다. 메인 HUD 와 떨어진 프리펩으로 HUD 캔버스에 붙고, 자기 Canvas(정렬 80)로 툴킷 HUD 아래에 깔린다 |
| 던전 HUD·던전 화면(골드·미궁·균열·유적) | 던전 폐지 예정이라 옮기지 않는다. 함께 쓰는 `SimplePopup` 도 남긴다 |
| 타이틀 씬 로그인 버튼(`TitleHUD`) | 씬에 직접 놓인 UI 다 |

---

## 11. 전환 순서

| 단계 | 내용 | 완료 기준 |
|---|---|---|
| 0. 기반 | 층 UIDocument 5개, `ToolkitScreen`, `UIPopup` + `ShowPopupAsync`, 공통 컴포넌트. 장비·마스터리 화면을 이 구조로 이전 | UGUI 창과 UI Toolkit 창이 섞여 열려도 그리는 순서·입력·뒤로가기가 정상 |
| 1. 창 | 상점 · 월드 · 제작 · 코스튬 · 펫 | 화면별 플레이 확인 |
| 2. 팝업 | 공통 → 보상 → 아이템 정보 → 나머지 | 팝업별 플레이 확인 |
| 3. 상시 UI | 네비게이션 바 · 알림 메시지 · 로딩 · 네트워크 딤 | 층 정렬 확인 |
| 4. HUD | 메인 HUD · 획득 로그 (조이스틱은 UGUI 프리펩으로 분리) | 마을·필드·보스 전투에서 확인 |
| 5. 정리 | 옛 UGUI 화면·슬롯 스크립트와 프리펩 삭제 | 삭제한 스크립트를 참조하는 에셋 없음, 컴파일 확인 |

- 층·프리펩은 에디터 메뉴 `ProjectOne/UI Toolkit/층·프리펩 전체 만들기`(`ToolkitSetupBuilder`)로 만든다. 새 UI Toolkit 화면·팝업을 추가하면 이 빌더에 함께 등록한다.

### 11.1 스냅샷 도구

- 에디터 메뉴로 화면별 샘플 데이터를 채워 1080 x 1920 PNG 로 찍는 도구를 둔다(편집 모드 렌더).
- 전환 단계마다 화면을 찍어 배치가 깨졌는지 확인한다. 글자 크기·테마를 바꿀 때도 전 화면을 다시 찍는다.
