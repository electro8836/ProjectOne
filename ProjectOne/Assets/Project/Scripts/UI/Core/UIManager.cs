using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Audio;
using ProjectOne.Utils;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Mail;
using ProjectOne.Ranking;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// UI 전역 진입점. 씬을 가로지르는 UI Toolkit 층(Layer)을 소유한다.
	//
	//   HUD    (90)   상시 HUD          — 조이스틱 · MainHUD · 던전 HUD · 타이틀
	//   Window (200)  전체창            — 장비 / 상점 / 던전 결과
	//   Navi   (300)  창 위 상시        — 네비게이션 바
	//   Popup  (350)  네비게이션 위     — 아이템 정보 · 확인
	//   System (400)  최상위            — 네트워크 딤 · 알림 메시지
	//
	// 위로 갈수록 덮는다. HUD 를 가장 아래에 두어 창·팝업이 자연히 그 위에 뜬다.
	// 화면 GameObject 는 모두 UIManager 아래에 만들고, 그 레이아웃만 층에 붙인다.
	// UGUI 는 유닛 머리 위 월드 UI(Canvas_World)에만 남아 있다.
	// GameStateChangedEvent 를 구독해 열린 창을 자동 정리한다.
	public class UIManager : MonoSingleton<UIManager>
	{
		// WorldSpace — 유닛 머리 위에 뜨는 게이지들. 화면이 아니라 월드에 그려진다.
		// 중첩 Canvas 는 루트의 renderMode 를 따르므로 반드시 다른 캔버스의 자식이 아니라
		// UIManager 직속(=Canvas 없는 루트의 자식)이어야 WorldSpace 로 산다.
		[SerializeField] private Canvas _worldCanvas;

		// UI Toolkit 층 — 층마다 정렬 값이 다른 PanelSettings 를 쓴다(UI구조_설계.md 2.1).
		[Header("UI Toolkit 층")]
		[SerializeField] private UIDocument _hudLayer;			// 90
		[SerializeField] private UIDocument _windowLayer;		// 200
		[SerializeField] private UIDocument _navigationLayer;	// 300
		[SerializeField] private UIDocument _popupLayer;		// 350
		[SerializeField] private UIDocument _systemLayer;		// 400

		// UI Toolkit 클릭음 — FMOD event:/SFX/<이름>. 옛 UGUI 버튼과 같은 소리다.
		[Header("UI Toolkit 클릭음")]
		[SerializeField] private string _clickSfx = "block_small_73";
		[SerializeField] private string _tabClickSfx = "block_small_69";

		// 이 시간(초) 안에 응답이 오면 딤을 보이지 않는다 — 응답이 실제로 지연될 때만 보인다.
		// 그동안에도 입력은 투명 차단막으로 즉시 막는다(응답 전 연타·탭 전환·창 닫기 방지).
		private const float NetworkBlockerShowDelaySec = 0.3f;

		// 열린 창 스택 (Back키 처리, 직렬 닫기용)
		// 주소를 함께 들고 있어야 닫을 때 참조카운트를 되돌릴 수 있다 — 화면만으로는 무엇을 Acquire 했는지 모른다.
		private readonly Stack<WindowEntry> _windowStack = new Stack<WindowEntry>();

		// 열린 창 하나. 화면과 그 화면을 불러온 Addressable 주소를 짝지어 둔다.
		private readonly struct WindowEntry
		{
			public readonly UIScreen screen;
			public readonly string address;

			public WindowEntry(UIScreen screen, string address)
			{
				this.screen = screen;
				this.address = address;
			}
		}

		// 씬에 직접 배치된 HUD 등 화면 레지스트리 — 깨어날 때 등록/사라질 때 해제하여 타입으로 O(1) 조회
		private readonly Dictionary<System.Type, UIScreen> _screens = new Dictionary<System.Type, UIScreen>();

		// 현재 진행 중인 팝업의 CancellationTokenSource
		private CancellationTokenSource _popupCts;

		// 재화 목록 팝업도 별도 CTS 를 쓴다. 이 팝업의 칸은 설명 툴팁을 띄우는데,
		// 툴팁이 _popupCts 를 취소하면 그것을 띄운 목록 팝업 자신이 닫혀 버린다.
		private CancellationTokenSource _currencyListCts;

		// 퀘스트 목록 팝업도 마찬가지다. 보상 칸을 누르면 장비·소모품 팝업이나 설명 툴팁이 뜨는데,
		// 그것들이 _popupCts 를 취소하면 목록 팝업 자신이 닫혀 버린다.
		private CancellationTokenSource _questListCts;

		// 출석 팝업도 같다 — 보상 칸을 누르면 설명 툴팁이 뜬다.
		private CancellationTokenSource _dailyBonusCts;

		// 히어로패스 팝업도 같다 — 보상 칸을 누르면 설명 툴팁이 뜬다.
		private CancellationTokenSource _heroPassCts;

		// 랭킹 팝업은 슬롯을 누르면 플레이어 정보 팝업이, 정보 팝업은 장비 칸을 누르면 장비 팝업이 위에 뜬다.
		// 위에 뜨는 팝업이 아래 팝업의 CTS 를 취소하면 안 되므로 둘 다 전용 CTS 를 쓴다.
		private CancellationTokenSource _rankingCts;
		private CancellationTokenSource _playerInfoCts;

		// 공용 보상 팝업은 다른 팝업(균열 소탕 등) 위에 뜬다 — 아래 팝업의 CTS 를 취소하면 안 되므로 전용이다.
		private CancellationTokenSource _rewardPopupCts;

		// 균열 팝업도 전용이다 — 소탕 보상 팝업의 칸을 누르면 정보 팝업이 _popupCts 를 취소하는데,
		// 같이 쓰면 그 아래의 균열 팝업까지 닫혀 버린다.
		private CancellationTokenSource _riftPopupCts;

		// 메일함 팝업은 슬롯을 누르면 메일 팝업이, 메일 팝업은 수령하면 보상 팝업이 위에 뜬다.
		// 위에 뜨는 팝업이 아래 팝업의 CTS 를 취소하면 안 되므로 둘 다 전용 CTS 를 쓴다.
		private CancellationTokenSource _mailBoxCts;
		private CancellationTokenSource _mailSlotCts;

		// 계정 팝업도 전용이다 — 위에 뜨는 닉네임 변경 팝업이 _popupCts 를 취소한다.
		private CancellationTokenSource _accountPopupCts;

		// 네트워크 딤 — System 층에 1회 만들어 두고 display 로 켜고 끈다.
		// 차단막(_networkBlocker)은 늘 입력을 막고, 보이는 내용(_networkBlockerContent)만 지연 뒤 보인다.
		private VisualElement _networkBlocker;
		private VisualElement _networkBlockerContent;
		private VisualElement _networkBlockerSpinner;
		private IVisualElementScheduledItem _networkBlockerSpin;
		// 동시 네트워크 요청 참조카운트 — 0이 되면 딤을 닫는다.
		private int _blockerRefCount;
		// 지연 표시 코루틴 — 닫힘 시 중지
		private Coroutine _blockerDelayCo;
		// 딤 안의 문구. 평소 응답 대기에는 쓰지 않고 꺼 둔다 — ShowNetworkMessage 로만 켠다.
		private Label _networkBlockerText;
		private bool _networkMessageShown;

		protected override void Awake()
		{
			base.Awake();
			ensureEventSystem();
			validateCanvases();
			validateToolkitLayers();
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
		}

		// EventSystem 이 없으면 UI 포인터 이벤트가 **발생 자체를 하지 않는다** —
		// 조이스틱 드래그(IDragHandler)도 버튼 클릭도 통째로 죽는다.
		//
		// 씬마다 배치하지 않고 여기서 보장하는 이유:
		//  - 씬이 5개고 앞으로 늘어난다. 하나만 빠뜨려도 그 씬에서 입력이 죽는다
		//  - UIManager 는 부트 1회 로드 + DontDestroyOnLoad 라 한 번이면 전 씬에 적용된다
		// 이미 있으면 만들지 않으므로 중복 경고가 나지 않는다.
		private void ensureEventSystem()
		{
			if (UnityEngine.EventSystems.EventSystem.current != null)
			{
				return;
			}

			GameObject go = new GameObject("EventSystem");
			go.transform.SetParent(transform, false);
			go.AddComponent<UnityEngine.EventSystems.EventSystem>();

			// 이 프로젝트는 새 Input System 을 쓴다(HeroController 등).
			// 구 StandaloneInputModule 은 InputSystem 활성 시 예외를 던진다.
			go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
		}

		// 월드 캔버스 배선이 끊기면 그 캔버스를 쓰는 모든 경로가 NRE 로 죽는다.
		// 증상은 "UI 가 안 뜬다" 로만 보여서 프리팹부터 뒤지게 되므로, 어느 것이 비었는지 여기서 밝힌다.
		//
		// 특히 직렬화 필드를 개명하면 유니티가 이름을 키로 쓰기 때문에 프리팹의 값이 조용히 떨어져 나간다.
		// 그 사고를 부팅 첫 줄에서 잡는 것이 목적이다.
		private void validateCanvases()
		{
			if (_worldCanvas == null)
			{
				Debug.LogError("[UIManager] _worldCanvas 가 비었습니다 — 월드 게이지를 띄울 수 없습니다.");
			}
		}

		// validateCanvases 와 같은 이유 — 직렬화 연결이 끊기면 부팅 첫 줄에서 밝힌다.
		private void validateToolkitLayers()
		{
			validateToolkitLayer(_hudLayer, "_hudLayer");
			validateToolkitLayer(_windowLayer, "_windowLayer");
			validateToolkitLayer(_navigationLayer, "_navigationLayer");
			validateToolkitLayer(_popupLayer, "_popupLayer");
			validateToolkitLayer(_systemLayer, "_systemLayer");
		}

		private static void validateToolkitLayer(UIDocument layer, string fieldName)
		{
			if (layer == null)
			{
				Debug.LogError($"[UIManager] {fieldName} 가 비었습니다 — 그 층의 UI Toolkit 화면을 띄울 수 없습니다.");
			}
		}

		// 층 루트. 빈 곳을 누르면 아래 층(UGUI 포함)으로 통과시킨다 — 화면이 붙은 곳만 입력을 받는다.
		// 클릭음도 여기서 층마다 한 번 건다(같은 콜백은 중복 등록되지 않는다).
		// Awake 에서 하지 않는 이유: 자식 UIDocument 의 OnEnable 이 아직 돌지 않아 rootVisualElement 가 없다.
		private VisualElement getLayerRoot(UIDocument layer)
		{
			VisualElement root = layer.rootVisualElement;
			root.pickingMode = PickingMode.Ignore;
			root.RegisterCallback<ClickEvent>(onLayerClicked);
			return root;
		}

		// ── 클릭음 ─────────────────────────────────────────────────────
		//
		// 버튼마다 연결하지 않고 층 루트에서 올라오는(bubble) 클릭을 받아 한꺼번에 낸다.
		// 끌어서 스크롤한 뒤의 클릭은 ScrollDragManipulator 가 삼키므로 여기까지 오지 않는다.
		// 소리 대상: Button · 탭(.tab / .nav-tab) · 버튼이 아니지만 누르는 요소(.sfx-click — 아이템 칸, 목록 행 등).

		private void onLayerClicked(ClickEvent evt)
		{
			VisualElement element = evt.target as VisualElement;
			while (element != null && element != evt.currentTarget)
			{
				if (element.ClassListContains("tab") == true || element.ClassListContains("nav-tab") == true)
				{
					playClickSfx(_tabClickSfx);
					return;
				}

				if (element is Button || element.ClassListContains("sfx-click") == true)
				{
					playClickSfx(_clickSfx);
					return;
				}

				element = element.parent;
			}
		}

		// 클릭 이벤트 없이 버튼음을 낸다 — 누른 채 반복(HoldRepeatManipulator)이 반복마다 부른다.
		public void PlayClickSfx()
		{
			playClickSfx(_clickSfx);
		}

		private static void playClickSfx(string sfxName)
		{
			if (string.IsNullOrEmpty(sfxName) == false && AudioManager.HasInstance == true)
			{
				AudioManager.Instance.PlaySFX(sfxName);
			}
		}

		// 팝업 프리펩을 UIManager 아래에 만들고 팝업 층에 붙인다.
		private GameObject spawnPopup(GameObject prefab)
		{
			GameObject go = Instantiate(prefab, this.transform);
			ToolkitScreen toolkit = go.GetComponent<ToolkitScreen>();
			if (toolkit != null && _popupLayer != null)
			{
				toolkit.Attach(getLayerRoot(_popupLayer));
			}

			return go;
		}

		// 씬에 놓인 화면(타이틀)을 HUD 층에 붙인다. 떼는 것은 화면의 OnDestroy 가 한다.
		public void AttachToHudLayer(ToolkitScreen screen)
		{
			attachToLayer(screen, _hudLayer);
		}

		// UI Toolkit 화면을 층 루트에 붙인다.
		private void attachToLayer(UIScreen screen, UIDocument layer)
		{
			ToolkitScreen toolkit = screen as ToolkitScreen;
			if (toolkit == null || layer == null)
			{
				return;
			}

			toolkit.Attach(getLayerRoot(layer));
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
			base.OnDestroy();
		}

		// ── 화면(HUD) 레지스트리 ────────────────────────────────────────
		// 씬에 직접 배치된 HUD가 Awake에서 자신을 등록하고 OnDestroy에서 해제한다.
		// 게임플레이 측(예: DungeonDirector)이 FindAnyObjectByType 없이 화면을 조회하는 용도.

		public void RegisterScreen(UIScreen screen)
		{
			if (screen == null)
			{
				return;
			}

			_screens[screen.GetType()] = screen;
		}

		public void UnregisterScreen(UIScreen screen)
		{
			if (screen == null)
			{
				return;
			}

			System.Type type = screen.GetType();
			if (_screens.TryGetValue(type, out UIScreen current) && current == screen)
			{
				_screens.Remove(type);
			}
		}

		public T GetScreen<T>() where T : UIScreen
		{
			if (_screens.TryGetValue(typeof(T), out UIScreen screen))
			{
				return screen as T;
			}

			return null;
		}

		// ── 네트워크 딤(블로커) ─────────────────────────────────────────
		// 뒤끝 호출(BackndFunctionCaller)이 응답 대기 동안 입력을 막기 위해 호출한다.
		// 참조카운트로 동시/연속 요청을 견딘다. 요청 즉시 투명하게 켜서 입력을 막고,
		// 지연 시간 내 응답이 오면 딤을 보이지 않은 채 닫는다.

		// 요청 시작 — 참조카운트를 올리고 첫 요청이면 투명 차단 후 지연 표시를 예약한다.
		public void ShowNetworkBlocker()
		{
			_blockerRefCount++;
			if (_blockerRefCount > 1)
			{
				return;	// 이미 표시(또는 지연 대기) 중
			}

			// 0→1: 투명 차단 후 지연 표시 시작. 직전 대기가 남아있지 않게 정리 후 재시작.
			if (_blockerDelayCo != null)
			{
				StopCoroutine(_blockerDelayCo);
			}

			ensureNetworkBlocker();

			// 문구가 떠 있는 중이면 이미 보이는 상태다 — 다시 숨기지 않는다.
			if (_networkMessageShown == false)
			{
				setBlockerVisible(false);
			}

			setBlockerShown(true);

			_blockerDelayCo = StartCoroutine(showBlockerDelayed());
		}

		// 요청 종료 — 참조카운트를 내리고 0이 되면 딤을 닫는다.
		public void HideNetworkBlocker()
		{
			if (_blockerRefCount <= 0)
			{
				return;	// 짝이 맞지 않는 호출 방지
			}

			_blockerRefCount--;
			if (_blockerRefCount > 0)
			{
				return;	// 아직 대기 중인 요청이 남음
			}

			// 0: 지연 대기 중이면 중지, 표시 중이면 닫는다.
			if (_blockerDelayCo != null)
			{
				StopCoroutine(_blockerDelayCo);
				_blockerDelayCo = null;
			}

			// 문구가 떠 있으면 그쪽이 닫을 때까지 둔다.
			if (_networkBlocker != null && _networkMessageShown == false)
			{
				setBlockerShown(false);
			}
		}

		// 딤에 문구를 띄운다(재연결 안내 등) — 지연 없이 바로 보이고 입력을 막는다. 버튼은 없다.
		// 응답 대기(Show/HideNetworkBlocker)와 별개로 돌며, 둘 다 끝나야 딤이 닫힌다.
		public void ShowNetworkMessage(string message)
		{
			ensureNetworkBlocker();

			_networkMessageShown = true;
			_networkBlockerText.text = message;
			_networkBlockerText.style.display = DisplayStyle.Flex;

			setBlockerShown(true);
			setBlockerVisible(true);
		}

		public void HideNetworkMessage()
		{
			if (_networkMessageShown == false)
			{
				return;
			}

			_networkMessageShown = false;
			_networkBlockerText.style.display = DisplayStyle.None;

			if (_blockerRefCount <= 0)
			{
				setBlockerShown(false);
				return;
			}

			// 응답 대기가 남아 있다 — 아직 지연 중이면 다시 투명 차단으로 돌린다.
			if (_blockerDelayCo != null)
			{
				setBlockerVisible(false);
			}
		}

		// 딤은 최초 1회만 만들어 System 층에 붙여 두고, 이후 켜고 끄기만 한다.
		private void ensureNetworkBlocker()
		{
			if (_networkBlocker != null)
			{
				return;
			}

			_networkBlocker = new VisualElement();
			_networkBlocker.AddToClassList("net-blocker");

			_networkBlockerContent = new VisualElement();
			_networkBlockerContent.AddToClassList("net-blocker__content");
			_networkBlockerContent.pickingMode = PickingMode.Ignore;
			_networkBlocker.Add(_networkBlockerContent);

			_networkBlockerSpinner = new VisualElement();
			_networkBlockerSpinner.AddToClassList("net-blocker__spinner");
			_networkBlockerSpinner.pickingMode = PickingMode.Ignore;
			_networkBlockerContent.Add(_networkBlockerSpinner);

			// 문구는 필요할 때만 켠다 — 평소 응답 대기는 회전 아이콘만 보인다.
			_networkBlockerText = new Label();
			_networkBlockerText.AddToClassList("net-blocker__text");
			_networkBlockerText.pickingMode = PickingMode.Ignore;
			_networkBlockerText.style.display = DisplayStyle.None;
			_networkBlockerContent.Add(_networkBlockerText);

			_networkBlockerSpin = _networkBlockerSpinner.schedule.Execute(spinNetworkBlocker).Every(50);
			_networkBlockerSpin.Pause();

			_networkBlocker.style.display = DisplayStyle.None;
			getLayerRoot(_systemLayer).Add(_networkBlocker);
		}

		// 켜면 층 맨 위로 올린다 — 알림 메시지보다 위에서 입력을 막는다.
		private void setBlockerShown(bool shown)
		{
			if (shown == true)
			{
				_networkBlocker.BringToFront();
				_networkBlockerSpin.Resume();
			}
			else
			{
				_networkBlockerSpin.Pause();
			}

			_networkBlocker.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private void spinNetworkBlocker()
		{
			float angle = _networkBlockerSpinner.style.rotate.value.angle.value + 30f;
			_networkBlockerSpinner.style.rotate = new Rotate(new Angle(angle % 360f));
		}

		// 지연 후 딤 표시 — 지연 동안 모든 요청이 끝나면(refCount 0) 표시하지 않는다.
		private IEnumerator showBlockerDelayed()
		{
			// 실시간으로 기다린다 — 계속하기 팝업 등 timeScale 0 상태에서도 딤이 보여야 한다.
			yield return new WaitForSecondsRealtime(NetworkBlockerShowDelaySec);
			_blockerDelayCo = null;

			if (_blockerRefCount <= 0)
			{
				yield break;
			}

			setBlockerVisible(true);
		}

		// 차단막은 그대로 두고 보이기만 바꾼다 — 투명한 상태에서도 차단막이 입력을 막는다.
		private void setBlockerVisible(bool visible)
		{
			_networkBlocker.EnableInClassList("net-blocker--visible", visible);
		}

		// ── 영속 MainHUD ────────────────────────────────────────────────
		//
		// 씬별 HUD 를 쓰지 않는다. 하나가 씬을 가로질러 살아 있고, 어디서 무엇을 보일지는
		// HudContext 가시성으로 정한다 (MainHud.ApplyContext).

		private const string MainHudAddress = "UIPrefab_MainHUD";
		private const string JoystickAddress = "UIPrefab_Joystick";

		private MainHud _mainHud;

		// 이동 조이스틱 — HUD 층(90)에 MainHUD 보다 먼저 붙여 그 아래에 깐다. HUD 의 빈 곳 터치만 받는다.
		private JoystickController _joystick;

		// MainHUD 안의 보스 배너. 던전 전용 HUD 가 자리를 양보할 대상을 찾는 유일한 경로다.
		public HudBossBanner BossUI
		{
			get { return (_mainHud != null) ? _mainHud.BossBanner : null; }
		}

		// 마을 진입 시 1회. 이미 떠 있으면 아무것도 하지 않는다.
		//
		// **히어로 스폰보다 먼저 불려야 한다.** 조이스틱·HUD 위젯이 UnitSpawnedEvent 를 구독하므로,
		// 히어로가 뜬 뒤에 HUD 가 생기면 이벤트를 놓쳐 영영 대상을 못 찾는다.
		public async UniTask EnsureMainHudAsync(CancellationToken ct)
		{
			if (_mainHud != null)
			{
				return;
			}

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(MainHudAddress, ct);
			if (prefab == null)
			{
				// HUD 가 아직 없어도 게임 흐름 자체는 막지 않는다.
				Debug.LogWarning($"[UIManager] MainHUD 프리팹을 찾지 못했습니다: {MainHudAddress}");
				return;
			}

			GameObject joystickPrefab = await ResourceManager.Instance.AcquireAsync<GameObject>(JoystickAddress, ct);
			if (joystickPrefab == null)
			{
				Debug.LogError($"[UIManager] 조이스틱 프리팹을 찾지 못했습니다: {JoystickAddress}");
			}
			else
			{
				_joystick = Instantiate(joystickPrefab, this.transform).GetComponent<JoystickController>();
				attachToLayer(_joystick, _hudLayer);
			}

			// HUD 층(90) — 창(200)·네비(300)·팝업(350)·시스템(400)이 자연히 위를 덮는다.
			// UIManager 자신이 영속이라 별도 DontDestroyOnLoad 가 필요 없다.
			_mainHud = Instantiate(prefab, this.transform).GetComponent<MainHud>();
			attachToLayer(_mainHud, _hudLayer);
		}

		// ── 타이틀 복귀 ────────────────────────────────────────────────

		// 타이틀로 돌려보낼 때 — 타이틀에 처음 들어온 것처럼 영속 UI 를 전부 걷는다.
		// 걷어 둔 것은 다음 마을 진입의 Ensure* 가 다시 세운다.
		public void ResetForTitle()
		{
			CloseAllWindowsAsync().Forget();

			// 떠 있는 팝업 — 취소를 받은 Show 메서드가 스스로 파괴·해제한다.
			cancelPopup(ref _popupCts);
			cancelToolkitPopups();
			cancelPopup(ref _currencyListCts);
			cancelPopup(ref _questListCts);
			cancelPopup(ref _dailyBonusCts);
			cancelPopup(ref _heroPassCts);
			cancelPopup(ref _rankingCts);
			cancelPopup(ref _playerInfoCts);
			cancelPopup(ref _rewardPopupCts);
			cancelPopup(ref _riftPopupCts);
			cancelPopup(ref _mailBoxCts);
			cancelPopup(ref _mailSlotCts);
			cancelPopup(ref _accountPopupCts);

			releaseMainHud();
			ReleaseDungeonHud();
			ReleaseWorldGauge();
			releaseNavigationBar();
			releaseNoticeMessages();
			resetNetworkBlocker();
		}

		// UI Toolkit 팝업을 모두 취소한다. 취소가 그 자리에서 이어져 사전을 고칠 수 있어 먼저 복사해 둔다.
		private void cancelToolkitPopups()
		{
			List<CancellationTokenSource> list = new List<CancellationTokenSource>(_toolkitPopupCts.Values);
			for (int i = 0; i < list.Count; i++)
			{
				list[i].Cancel();
			}
		}

		private static void cancelPopup(ref CancellationTokenSource cts)
		{
			if (cts == null)
			{
				return;
			}

			cts.Cancel();
			cts.Dispose();
			cts = null;
		}

		// HUD 는 맥락이 None 이어도 재화 바·조이스틱 같은 상시 위젯이 남아 타이틀을 덮는다.
		private void releaseMainHud()
		{
			if (_joystick != null)
			{
				Destroy(_joystick.gameObject);
				_joystick = null;
				ResourceManager.Instance.Release(JoystickAddress);
			}

			if (_mainHud == null)
			{
				return;
			}

			Destroy(_mainHud.gameObject);
			_mainHud = null;
			ResourceManager.Instance.Release(MainHudAddress);
		}

		private void releaseNavigationBar()
		{
			if (_navigationBar == null)
			{
				return;
			}

			Destroy(_navigationBar);
			_navigationBar = null;
			_navigationBarView = null;
			ResourceManager.Instance.Release(NavigationBarAddress);
		}

		private void releaseNoticeMessages()
		{
			releaseNotice(_systemLogInfo, SystemLogInfoAddress);
			releaseNotice(_alertMessage, AlertMessageAddress);
			releaseNotice(_levelUpMessage, LevelUpMessageAddress);
			releaseNotice(_systemMessage, SystemMessageAddress);
			releaseNotice(_warningMessage, WarningMessageAddress);

			_systemLogInfo = null;
			_alertMessage = null;
			_levelUpMessage = null;
			_systemMessage = null;
			_warningMessage = null;
		}

		private static void releaseNotice(MonoBehaviour view, string address)
		{
			if (view == null)
			{
				return;
			}

			Destroy(view.gameObject);
			ResourceManager.Instance.Release(address);
		}

		// 딤은 파괴하지 않는다 — 꺼진 상태로만 되돌린다.
		private void resetNetworkBlocker()
		{
			_blockerRefCount = 0;
			_networkMessageShown = false;

			if (_blockerDelayCo != null)
			{
				StopCoroutine(_blockerDelayCo);
				_blockerDelayCo = null;
			}

			if (_networkBlocker != null)
			{
				_networkBlockerText.style.display = DisplayStyle.None;
				setBlockerShown(false);
			}
		}

		// ── 던전 전용 HUD ──────────────────────────────────────────────
		//
		// 던전마다 필요한 위젯이 달라 MainHUD 한 프리팹에 다 넣지 않는다. 들어간 던전의 것만 만들어
		// HUD 층의 **마지막 자식**으로 붙인다 — 같은 층에서는 뒤에 붙은 것이 위에 그려진다.
		//
		// 수명은 던전 한 판이다. DungeonDirector.Begin 이 만들고 cleanupAll 이 걷는다.

		private GameObject _dungeonHud;
		private string _dungeonHudAddress;

		// 던전 공용 메시지(클리어·실패·진행도·경고) — 던전 HUD 와 같은 수명으로 HUD 위에 붙는다.
		private const string DungeonMessageAddress = "UIPrefab_DungeonMessage";
		private DungeonMessage _dungeonMessage;

		// 던전 진행 중에만 있다. 없으면 null.
		public DungeonMessage DungeonMessage
		{
			get { return _dungeonMessage; }
		}

		// 던전 종류 → 프리팹 주소. 모드가 없는 던전은 빈 문자열이다 — 구현되면 여기에 한 줄 늘린다.
		private static string getDungeonHudAddress(EDT.Dungeon type)
		{
			switch (type)
			{
				case EDT.Dungeon.Gold:
					return "UIPrefab_GoldDungeon";
				case EDT.Dungeon.Rift:
					return "UIPrefab_RiftDungeon";
				case EDT.Dungeon.Labyrinth:
					return "UIPrefab_LabyrinthDungeon";
				case EDT.Dungeon.Ruins:
					return "UIPrefab_RuinsDungeon";
			}

			return string.Empty;
		}

		// 던전 진입 시 1회. **모드 시작(startStage)보다 먼저 불려야 한다** — 웨이브 알림을 놓치면
		// 첫 배너가 통째로 사라진다.
		public async UniTask EnsureDungeonHudAsync(EDT.Dungeon type, CancellationToken ct)
		{
			string address = getDungeonHudAddress(type);
			if (string.IsNullOrEmpty(address) == true)
			{
				// 아직 전용 UI 가 없는 던전 — 띄울 것이 없을 뿐 흐름은 막지 않는다.
				return;
			}

			// 같은 던전의 다음 단계로 재진입하는 경로 — 이미 떠 있는 것을 그대로 쓴다.
			if (_dungeonHud != null && _dungeonHudAddress == address)
			{
				return;
			}

			ReleaseDungeonHud();

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				// HUD 가 없어도 던전 진행 자체는 막지 않는다 (MainHUD 와 같은 규약).
				Debug.LogWarning($"[UIManager] 던전 HUD 프리팹을 찾지 못했습니다: {address}");
				return;
			}

			_dungeonHud = Instantiate(prefab, this.transform);
			attachToLayer(_dungeonHud.GetComponent<UIScreen>(), _hudLayer);
			_dungeonHudAddress = address;

			await ensureDungeonMessageAsync(ct);
		}

		// 메시지는 HUD 보다 위에 그린다 — HUD 다음 자식으로 붙인다.
		private async UniTask ensureDungeonMessageAsync(CancellationToken ct)
		{
			if (_dungeonMessage != null)
			{
				attachToLayer(_dungeonMessage, _hudLayer);	// 다시 붙이면 층의 맨 뒤(맨 위)로 옮겨진다
				return;
			}

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(DungeonMessageAddress, ct);
			if (prefab == null)
			{
				Debug.LogWarning($"[UIManager] 던전 메시지 프리팹을 찾지 못했습니다: {DungeonMessageAddress}");
				return;
			}

			GameObject go = Instantiate(prefab, this.transform);
			_dungeonMessage = go.GetComponent<DungeonMessage>();
			if (_dungeonMessage == null)
			{
				Debug.LogError($"[UIManager] {DungeonMessageAddress} 에 DungeonMessage 가 없습니다.");
				Destroy(go);
				ResourceManager.Instance.Release(DungeonMessageAddress);
				return;
			}

			attachToLayer(_dungeonMessage, _hudLayer);
		}

		// 던전 종료 시. UIManager 가 영속이라 명시적으로 걷지 않으면 마을까지 따라간다.
		public void ReleaseDungeonHud()
		{
			releaseDungeonMessage();

			if (_dungeonHud == null)
			{
				return;
			}

			Destroy(_dungeonHud);
			_dungeonHud = null;

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_dungeonHudAddress);
			}

			_dungeonHudAddress = null;
		}

		private void releaseDungeonMessage()
		{
			if (_dungeonMessage == null)
			{
				return;
			}

			Destroy(_dungeonMessage.gameObject);
			_dungeonMessage = null;

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(DungeonMessageAddress);
			}
		}

		// ── 영속 네비게이션 바 ──────────────────────────────────────────
		//
		// MainHUD 와 같은 영속 UI 지만 층이 다르다 — 창(200) 위에 상시 떠야 하므로
		// 전용 Navigation(300) 층에 붙인다. 어느 맥락에서 보일지는 NavigationBar 가 스스로 정한다.

		private const string NavigationBarAddress = "UIPrefab_NavigationBar";

		private GameObject _navigationBar;

		// 창이 가릴지 알려 줄 대상. 프리팹을 세울 때 한 번만 찾아 둔다.
		private NavigationBar _navigationBarView;

		// 마을 진입 시 1회. 이미 떠 있으면 아무것도 하지 않는다.
		public async UniTask EnsureNavigationBarAsync(CancellationToken ct)
		{
			if (_navigationBar != null)
			{
				return;
			}

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(NavigationBarAddress, ct);
			if (prefab == null)
			{
				// 네비게이션이 없어도 게임 흐름 자체는 막지 않는다.
				Debug.LogWarning($"[UIManager] NavigationBar 프리팹을 찾지 못했습니다: {NavigationBarAddress}");
				return;
			}

			_navigationBar = Instantiate(prefab, this.transform);
			_navigationBarView = _navigationBar.GetComponent<NavigationBar>();
			attachToLayer(_navigationBarView, _navigationLayer);

			// 네비게이션 바가 뒤늦게 서는 경우가 있다 — 이미 열려 있는 창의 요구를 바로 반영한다.
			applyNavigationBarVisibility();
		}

		// ── 알림 메시지 ────────────────────────────────────────────────
		//
		// 경고 · 레벨업 · 시스템 · 제한 네 종류가 각자 독립적으로 뜬다(동시에 떠도 된다).
		// 영속 UI 라 한 번 세우면 걷지 않는다. 위치는 각 프리펩의 앵커·좌표를 그대로 쓴다.
		// 레벨업 메시지는 레벨업 이벤트를 스스로 구독하므로 여기서 띄우는 API 가 없다.
		// 획득 로그(SystemLogInfo)도 같은 층에 둔다. 보상 획득 이벤트를 스스로 구독하므로 역시 API 가 없다.

		private const string SystemLogInfoAddress = "UIPrefab_SystemLogInfo";
		private const string AlertMessageAddress = "UIPrefab_AlertMessage";
		private const string LevelUpMessageAddress = "UIPrefab_LevelUpMessage";
		private const string SystemMessageAddress = "UIPrefab_SystemMessage";
		private const string WarningMessageAddress = "UIPrefab_WarningMessage";

		private SystemLogInfo _systemLogInfo;
		private NoticeMessage _alertMessage;
		private LevelUpMessage _levelUpMessage;
		private NoticeMessage _systemMessage;
		private WarningMessage _warningMessage;

		// 마을 진입 시 1회. 이미 떠 있으면 아무것도 하지 않는다.
		public async UniTask EnsureNoticeMessagesAsync(CancellationToken ct)
		{
			// 로그를 먼저 세운다 — 같은 층에서는 먼저 붙은 것이 아래에 그려지므로 알림 메시지가 로그를 덮는다.
			if (_systemLogInfo == null)
			{
				_systemLogInfo = await instantiateNoticeAsync<SystemLogInfo>(SystemLogInfoAddress, ct);
			}

			if (_alertMessage == null)
			{
				_alertMessage = await instantiateNoticeAsync<NoticeMessage>(AlertMessageAddress, ct);
			}

			if (_levelUpMessage == null)
			{
				_levelUpMessage = await instantiateNoticeAsync<LevelUpMessage>(LevelUpMessageAddress, ct);
			}

			if (_systemMessage == null)
			{
				_systemMessage = await instantiateNoticeAsync<NoticeMessage>(SystemMessageAddress, ct);
			}

			if (_warningMessage == null)
			{
				_warningMessage = await instantiateNoticeAsync<WarningMessage>(WarningMessageAddress, ct);
			}

			// 네트워크 딤이 먼저 만들어져 있었다면 메시지들이 그 위에 붙었다 — 딤을 다시 최상단으로.
			if (_networkBlocker != null)
			{
				_networkBlocker.BringToFront();
			}
		}

		// 경고 메시지 — 1줄 3초·2줄 5초 유지 후 사라진다. 떠 있는 중에 오면 닫힌 뒤 이어서 뜬다.
		public void ShowAlertMessage(string message)
		{
			if (_alertMessage == null)
			{
				Debug.LogWarning($"[UIManager] 경고 메시지가 아직 준비되지 않았습니다: {message}");
				return;
			}

			_alertMessage.Show(message);
		}

		// 시스템 메시지(공지 등) — 1줄 4초·2줄 6초 유지 후 사라진다. 여러 개면 차례로 뜬다.
		public void ShowSystemMessage(string message)
		{
			if (_systemMessage == null)
			{
				Debug.LogWarning($"[UIManager] 시스템 메시지가 아직 준비되지 않았습니다: {message}");
				return;
			}

			_systemMessage.Show(message);
		}

		// 제한 메시지 — HideWarningMessage 를 부를 때까지 떠 있다.
		public void ShowWarningMessage(string message)
		{
			if (_warningMessage == null)
			{
				Debug.LogWarning($"[UIManager] 제한 메시지가 아직 준비되지 않았습니다: {message}");
				return;
			}

			_warningMessage.Show(message);
		}

		public void HideWarningMessage()
		{
			if (_warningMessage == null)
			{
				return;
			}

			_warningMessage.Hide();
		}

		// UIManager 아래에 만들고 시스템 층에 붙인다.
		private async UniTask<T> instantiateNoticeAsync<T>(string address, CancellationToken ct) where T : MonoBehaviour
		{
			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				// 메시지가 없어도 게임 흐름 자체는 막지 않는다.
				Debug.LogWarning($"[UIManager] 알림 메시지 프리팹을 찾지 못했습니다: {address}");
				return null;
			}

			GameObject go = Instantiate(prefab, this.transform);

			// UI Toolkit 알림이면 System 층(400)에 붙인다.
			ToolkitScreen toolkit = go.GetComponent<ToolkitScreen>();
			if (toolkit != null && _systemLayer != null)
			{
				toolkit.Attach(getLayerRoot(_systemLayer));
			}

			T view = go.GetComponent<T>();
			if (view == null)
			{
				Debug.LogError($"[UIManager] {address} 루트에 {typeof(T).Name} 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return null;
			}

			return view;
		}

		// ── 월드 게이지 ────────────────────────────────────────────────
		//
		// 유닛 머리 위에 뜨는 게이지들. 화면 UI 와 달리 Canvas_World(WorldSpace) 아래에 붙는다.
		// 전투에서만 쓰이므로 전투 진입에 로드하고 나갈 때 놓는다 — 마을까지 들고 가지 않는다.

		private const string BossCastingGaugeAddress = "UIPrefab_BossCastingGage";
		private const string InteractionGaugeAddress = "UIPrefab_InteractionGage";
		private const string BossGimmickGaugeAddress = "UIPrefab_BossGimmickGage";

		private GameObject _bossCastingGaugeGo;
		private GameObject _interactionGaugeGo;
		private InteractionGauge _interactionGauge;

		// 기믹 게이지만 프리팹을 들고 있는다 — 코어마다 하나씩 찍어야 해서 인스턴스가 여럿이다.
		private GameObject _bossGimmickGaugePrefab;

		// 상호작용 진행 게이지 접근점 — 기믹 파훼·상자 개봉·자원 수집이 공용으로 쓴다.
		// 아직 로드 전이면 null 이므로 호출부가 확인해야 한다.
		public InteractionGauge InteractionGauge
		{
			get { return _interactionGauge; }
		}

		// 전투 진입 시 1회. 이미 떠 있으면 아무것도 하지 않는다.
		public async UniTask EnsureWorldGaugeAsync(CancellationToken ct)
		{
			if (_worldCanvas == null)
			{
				return;
			}

			if (_bossCastingGaugeGo == null)
			{
				_bossCastingGaugeGo = await instantiateWorldGaugeAsync(BossCastingGaugeAddress, ct);
			}

			if (_interactionGaugeGo == null)
			{
				_interactionGaugeGo = await instantiateWorldGaugeAsync(InteractionGaugeAddress, ct);
				if (_interactionGaugeGo != null)
				{
					_interactionGauge = _interactionGaugeGo.GetComponent<InteractionGauge>();
					if (_interactionGauge == null)
					{
						Debug.LogError($"[UIManager] {InteractionGaugeAddress} 에 InteractionGauge 컴포넌트가 없습니다.");
					}
				}
			}

			if (_bossGimmickGaugePrefab == null)
			{
				_bossGimmickGaugePrefab = await ResourceManager.Instance.AcquireAsync<GameObject>(BossGimmickGaugeAddress, ct);
				if (_bossGimmickGaugePrefab == null)
				{
					// 게이지가 없어도 파훼 자체는 돌아간다 — 진행도가 안 보일 뿐이다.
					Debug.LogWarning($"[UIManager] 월드 게이지 프리팹을 찾지 못했습니다: {BossGimmickGaugeAddress}");
				}
			}
		}

		// 기믹 코어 1개당 1개. 회수는 게이지가 스스로 한다 — 대상이 꺼지거나 사라지면 자기를 파괴한다.
		public BossGimmickGauge CreateBossGimmickGauge()
		{
			if (_bossGimmickGaugePrefab == null || _worldCanvas == null)
			{
				return null;
			}

			GameObject go = Instantiate(_bossGimmickGaugePrefab, _worldCanvas.transform);
			BossGimmickGauge gauge = go.GetComponent<BossGimmickGauge>();
			if (gauge == null)
			{
				Debug.LogError($"[UIManager] {BossGimmickGaugeAddress} 에 BossGimmickGauge 컴포넌트가 없습니다.");
				Destroy(go);
				return null;
			}

			return gauge;
		}

		// 전투 종료 시. UIManager 가 영속이라 명시적으로 걷지 않으면 마을까지 따라간다.
		public void ReleaseWorldGauge()
		{
			if (_bossCastingGaugeGo != null)
			{
				Destroy(_bossCastingGaugeGo);
				_bossCastingGaugeGo = null;
				ResourceManager.Instance.Release(BossCastingGaugeAddress);
			}

			if (_interactionGaugeGo != null)
			{
				Destroy(_interactionGaugeGo);
				_interactionGaugeGo = null;
				_interactionGauge = null;
				ResourceManager.Instance.Release(InteractionGaugeAddress);
			}

			if (_bossGimmickGaugePrefab != null)
			{
				// 찍어 둔 인스턴스는 각자 대상을 따라 사라진다 — 여기서는 프리팹 참조만 놓는다.
				_bossGimmickGaugePrefab = null;
				ResourceManager.Instance.Release(BossGimmickGaugeAddress);
			}
		}

		private async UniTask<GameObject> instantiateWorldGaugeAsync(string address, CancellationToken ct)
		{
			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				// 게이지가 없어도 전투 흐름 자체는 막지 않는다.
				Debug.LogWarning($"[UIManager] 월드 게이지 프리팹을 찾지 못했습니다: {address}");
				return null;
			}

			return Instantiate(prefab, _worldCanvas.transform);
		}

		// ── 창(Window) ──────────────────────────────────────────────────

		// 창 스택 최상단이 네비게이션 바를 가리는지에 따라 표시를 다시 맞춘다.
		// 스택이 비면 아무도 가리지 않는다. 네비게이션 바가 아직 없으면(던전 등) 할 일이 없다.
		private void applyNavigationBarVisibility()
		{
			if (_navigationBarView == null)
			{
				return;
			}

			bool hidden = false;
			if (_windowStack.Count > 0)
			{
				hidden = _windowStack.Peek().screen.HidesNavigationBar;
			}

			_navigationBarView.SetHiddenByWindow(hidden);
		}

		// 화면 열기의 단일 진입점. NPC 클릭이든 HUD 버튼이든 여기로 들어온다 —
		// 그래야 "굳이 NPC 에게 가지 않아도 같은 창이 열린다"가 한 벌의 코드로 성립한다.
		public async UniTask<UIScreen> OpenAsync(UIScreenId id, CancellationToken ct)
		{
			string address = UIScreenCatalog.GetAddress(id);
			if (string.IsNullOrEmpty(address) == true)
			{
				Debug.LogError($"[UIManager] {id} 의 주소가 UIScreenCatalog 에 없습니다.");
				return null;
			}

			return await OpenWindowAsync<UIScreen>(address, ct);
		}

		// Addressable 주소로 창을 열어 창 층에 붙인다.
		public async UniTask<T> OpenWindowAsync<T>(string address, CancellationToken ct) where T : UIScreen
		{
			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				return null;
			}

			GameObject go = Instantiate(prefab, this.transform);
			T screen = go.GetComponent<T>();
			if (screen == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return null;
			}

			attachToLayer(screen, _windowLayer);
			_windowStack.Push(new WindowEntry(screen, address));
			applyNavigationBarVisibility();

			await screen.OnOpenAsync(ct);
			return screen;
		}

		// 코드에서 네비게이션 탭을 이동한다 — 탭을 누른 것과 같이 창을 교체하고 선택 표시도 옮긴다.
		// 네비게이션 바가 없으면(아직 안 만들어졌거나 던전 등) 창 교체만 한다.
		public UniTask<UIScreen> OpenTabAsync(UIScreenId id, CancellationToken ct)
		{
			if (_navigationBarView != null)
			{
				return _navigationBarView.OpenTabAsync(id);
			}

			return SwitchWindowAsync(id, ct);
		}

		// 탭 전환 전용 — 새 창을 다 세운 뒤에 이전 창을 치운다.
		//
		// 닫고 나서 여는 순서로 하면 Addressable 로드를 기다리는 동안 창이 하나도 없는 화면이 그대로 보인다.
		// 새 창은 창 층의 마지막 자식으로 붙어 이전 창 위에 그려지므로 그 사이에도 빈 화면이 없다.
		public async UniTask<UIScreen> SwitchWindowAsync(UIScreenId id, CancellationToken ct)
		{
			string address = UIScreenCatalog.GetAddress(id);
			if (string.IsNullOrEmpty(address) == true)
			{
				Debug.LogError($"[UIManager] {id} 의 주소가 UIScreenCatalog 에 없습니다.");
				return null;
			}

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				return null;
			}

			GameObject go = Instantiate(prefab, this.transform);
			UIScreen screen = go.GetComponent<UIScreen>();
			if (screen == null)
			{
				// 아직 스택을 건드리기 전이다 — 실패해도 이전 창이 그대로 남는다.
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return null;
			}

			attachToLayer(screen, _windowLayer);

			// 새 창만 남기고, 밀려난 것들은 아래에서 정리한다.
			WindowEntry[] closing = _windowStack.ToArray();
			_windowStack.Clear();
			_windowStack.Push(new WindowEntry(screen, address));
			applyNavigationBarVisibility();

			await screen.OnOpenAsync(ct);

			// 새 창이 화면을 덮은 뒤에 이전 것을 치운다.
			// WindowClosedEvent 는 내지 않는다 — 새 창이 열려 있는데 발행하면 방금 누른 탭의 선택이 풀린다.
			for (int i = 0; i < closing.Length; i++)
			{
				await closing[i].screen.OnCloseAsync();
				Destroy(closing[i].screen.gameObject);
				releaseWindow(closing[i].address);
			}

			return screen;
		}

		// 스택 최상단 창을 닫는다.
		// publishWhenEmpty: 스택이 비었을 때 WindowClosedEvent를 발행할지.
		// 사용자 닫기(닫기 버튼)는 true(기본), 탭 전환·씬 전환의 일괄 닫기는 false로 조용히 닫는다.
		public async UniTask CloseWindowAsync(bool publishWhenEmpty = true)
		{
			if (_windowStack.Count == 0)
			{
				return;
			}

			WindowEntry entry = _windowStack.Pop();
			await entry.screen.OnCloseAsync();
			Destroy(entry.screen.gameObject);
			releaseWindow(entry.address);

			// 최상단이 바뀌었다 — 아래 창이 네비게이션 바를 가리지 않으면 다시 보인다.
			applyNavigationBarVisibility();

			// 마지막 창이 닫혀 스택이 비면 통지 (탭 그룹 등이 선택 해제).
			if (publishWhenEmpty && _windowStack.Count == 0)
			{
				EventManager.Instance.Publish(new WindowClosedEvent());
			}
		}

		// 창 프리팹의 참조카운트를 되돌린다.
		// 종료/씬 전환 흐름에서 ResourceManager 가 먼저 파괴됐으면 Instance 는 null 이라 가드가 필요하다.
		private static void releaseWindow(string address)
		{
			if (string.IsNullOrEmpty(address) == true || ResourceManager.HasInstance == false)
			{
				return;
			}

			ResourceManager.Instance.Release(address);
		}

		// 모든 창을 닫는다 (탭 전환·씬 전환 시 호출 — 조용히 닫음).
		public async UniTask CloseAllWindowsAsync()
		{
			while (_windowStack.Count > 0)
			{
				await CloseWindowAsync(false);
			}
		}

		// ── 공통 팝업 ───────────────────────────────────────────────────

		private const string BOX_REWARD_POPUP_ADDRESS = "UIPrefab_BoxRewardPopup";
		private const string STAT_POPUP_ADDRESS = "UIPrefab_StatPopup";
		private const string CURRENCY_LIST_POPUP_ADDRESS = "UIPrefab_CurrencyListPopup";
		private const string MENU_POPUP_ADDRESS = "UIPrefab_MenuPopup";
		private const string DUNGEON_LIST_POPUP_ADDRESS = "UIPrefab_DungeonListPopup";
		private const string QUEST_LIST_POPUP_ADDRESS = "UIPrefab_QuestListPopup";
		private const string DAILY_BONUS_POPUP_ADDRESS = "UIPrefab_DailyBonusPopup";
		private const string MAP_SCREEN_ADDRESS = "UIPrefab_MapScreen";
		private const string COSTUME_SCREEN_ADDRESS = "UIPrefab_CostumeInfo";
		private const string PET_SCREEN_ADDRESS = "UIPrefab_PetInfo";
		private bool _menuScreenOpening;
		private const string HERO_PASS_POPUP_ADDRESS = "UIPrefab_PassPopup";
		private const string RANKING_POPUP_ADDRESS = "UIPrefab_RankingPopup";
		private const string PLAYER_INFO_POPUP_ADDRESS = "UIPrefab_PlayerInfoPopup";
		private const string MAIL_BOX_POPUP_ADDRESS = "UIPrefab_MailBoxPopup";
		private const string MAIL_SLOT_POPUP_ADDRESS = "UIPrefab_MailSlotPopup";
		private const string NICKNAME_POPUP_ADDRESS = "UIPrefab_NicknamePopup";
		private const string DECOMPOSITION_POPUP_ADDRESS = "UIPrefab_DecompositionPopup";
		private const string ACCOUNT_POPUP_ADDRESS = "UIPrefab_AccountPopup";


		// 내 캐릭터 능력치 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// 프리펩이 하나뿐이라 주소는 상수로 둔다.
		public async UniTask ShowStatPopupAsync(CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(STAT_POPUP_ADDRESS, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			StatPopup popup = go.GetComponent<StatPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(STAT_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(STAT_POPUP_ADDRESS);
			}
		}

		// 메인 HUD 의 메뉴 팝업을 팝업 층에 열고 닫힘을 기다린다.
		// 메뉴는 닫히면서 다른 팝업으로 넘어간다 — 무엇으로 닫혔는지 돌려주고, 다음 팝업은 호출부가 띄운다.
		public async UniTask<MenuPopupResult> ShowMenuPopupAsync(CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(MENU_POPUP_ADDRESS, _popupCts.Token);
			if (prefab == null)
			{
				return MenuPopupResult.None;
			}

			GameObject go = spawnPopup(prefab);
			MenuPopup popup = go.GetComponent<MenuPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(MENU_POPUP_ADDRESS);
				return MenuPopupResult.None;
			}

			MenuPopupResult result = await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(MENU_POPUP_ADDRESS);
			}

			return result;
		}

		// 던전 고르기 팝업을 열고 고른 던전을 돌려준다. 그냥 닫으면 None.
		public async UniTask<EDT.Dungeon> ShowDungeonListPopupAsync(CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(DUNGEON_LIST_POPUP_ADDRESS, _popupCts.Token);
			if (prefab == null)
			{
				return EDT.Dungeon.None;
			}

			GameObject go = spawnPopup(prefab);
			DungeonListPopup popup = go.GetComponent<DungeonListPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(DUNGEON_LIST_POPUP_ADDRESS);
				return EDT.Dungeon.None;
			}

			EDT.Dungeon result = await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(DUNGEON_LIST_POPUP_ADDRESS);
			}

			return result;
		}

		// 닉네임 변경 팝업을 팝업 층에 열고 닫힘을 기다린다.
		public async UniTask ShowNicknamePopupAsync(CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(NICKNAME_POPUP_ADDRESS, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			NicknamePopup popup = go.GetComponent<NicknamePopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_NicknamePopup 루트에 NicknamePopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(NICKNAME_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(NICKNAME_POPUP_ADDRESS);
			}
		}

		// 계정 팝업을 팝업 층에 열고 닫힘을 기다린다. 전용 CTS 를 쓴다(_accountPopupCts 참고).
		public async UniTask ShowAccountPopupAsync(CancellationToken ct)
		{
			_accountPopupCts?.Cancel();
			_accountPopupCts?.Dispose();
			_accountPopupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(ACCOUNT_POPUP_ADDRESS, _accountPopupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			AccountPopup popup = go.GetComponent<AccountPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_AccountPopup 루트에 AccountPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(ACCOUNT_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_accountPopupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(ACCOUNT_POPUP_ADDRESS);
			}
		}

		// 메일함 팝업을 팝업 층에 열고 닫힘을 기다린다. 전용 CTS 를 쓴다(_mailBoxCts 참고).
		public async UniTask ShowMailBoxPopupAsync(CancellationToken ct)
		{
			_mailBoxCts?.Cancel();
			_mailBoxCts?.Dispose();
			_mailBoxCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(MAIL_BOX_POPUP_ADDRESS, _mailBoxCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			MailBoxPopup popup = go.GetComponent<MailBoxPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_MailBoxPopup 루트에 MailBoxPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(MAIL_BOX_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_mailBoxCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(MAIL_BOX_POPUP_ADDRESS);
			}
		}

		// 메일 한 통을 여는 팝업을 메일함 위에 열고 닫힘을 기다린다. 수령·삭제는 넘겨받은 공급자로 처리한다.
		public async UniTask ShowMailSlotPopupAsync(MailData mail, IMailProvider provider, CancellationToken ct)
		{
			_mailSlotCts?.Cancel();
			_mailSlotCts?.Dispose();
			_mailSlotCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(MAIL_SLOT_POPUP_ADDRESS, _mailSlotCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			MailSlotPopup popup = go.GetComponent<MailSlotPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_MailSlotPopup 루트에 MailSlotPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(MAIL_SLOT_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(mail, provider, _mailSlotCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(MAIL_SLOT_POPUP_ADDRESS);
			}
		}

		// 보유 재화 목록 팝업을 팝업 층에 열고 닫힘을 기다린다.
		public async UniTask ShowCurrencyListPopupAsync(CancellationToken ct)
		{
			_currencyListCts?.Cancel();
			_currencyListCts?.Dispose();
			_currencyListCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(CURRENCY_LIST_POPUP_ADDRESS, _currencyListCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			CurrencyListPopup popup = go.GetComponent<CurrencyListPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(CURRENCY_LIST_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_currencyListCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(CURRENCY_LIST_POPUP_ADDRESS);
			}
		}

		// 아이템 정보 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// stashMode = true 면 보관함↔인벤토리 이동 버튼이 함께 나온다(장비 화면의 보관함 모드).
		public async UniTask ShowItemInfoPopupAsync(string address, long uid, bool stashMode, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			ItemInfoPopup popup = go.GetComponent<ItemInfoPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(uid, stashMode, _popupCts.Token);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 장비 정보 팝업을 **디스플레이(읽기 전용)** 로 연다 — 결과창·상점처럼 내 것이 아닌 목록용.
		// 인벤토리를 조회하지 않으므로 보유하지 않은 인스턴스도 열린다.
		public async UniTask ShowItemInfoPopupAsync(string address, EquipmentInstance instance, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			ItemInfoPopup popup = go.GetComponent<ItemInfoPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(instance, _popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 소모품 정보 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// 소모품은 스택이라 장비처럼 인스턴스 UID 가 없어 아이템 ID 로 연다.
		// readOnly = true 면 디스플레이 경로다 — 수량 조절·사용·파괴를 감춘다.
		public async UniTask ShowConsumablePopupAsync(string address, int itemId, bool readOnly, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			ConsumableInfoPopup popup = go.GetComponent<ConsumableInfoPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(itemId, readOnly, _popupCts.Token);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 액트·필드 목록 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// 이동으로 닫혔으면 true — 호출부(월드 화면)가 그때 자기 창도 닫는다.
		public async UniTask<bool> ShowActListPopupAsync(string address, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return false;
			}

			GameObject go = spawnPopup(prefab);
			ActListPopup popup = go.GetComponent<ActListPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return false;
			}

			bool moved = await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}

			return moved;
		}

		// 골드던전 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		public async UniTask ShowGoldDungeonPopupAsync(string address, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			GoldDungeonPopup popup = go.GetComponent<GoldDungeonPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 미궁던전 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		public async UniTask ShowLabyrinthDungeonPopupAsync(string address, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			LabyrinthDungeonPopup popup = go.GetComponent<LabyrinthDungeonPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 유적던전 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		public async UniTask ShowRuinsDungeonPopupAsync(string address, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			RuinsDungeonPopup popup = go.GetComponent<RuinsDungeonPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 균열던전 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다. 전용 CTS 를 쓴다(_riftPopupCts 참고).
		public async UniTask ShowRiftDungeonPopupAsync(string address, CancellationToken ct)
		{
			_riftPopupCts?.Cancel();
			_riftPopupCts?.Dispose();
			_riftPopupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _riftPopupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			RiftDungeonPopup popup = go.GetComponent<RiftDungeonPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(_riftPopupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		private const string REWARD_POPUP_ADDRESS = "UIPrefab_RewardPopup";

		// 공용 보상 팝업 — 이미 지급된 보상 목록을 팝업 층에 띄우고 닫힘을 기다린다.
		public async UniTask ShowRewardPopupAsync(IReadOnlyList<ProjectOne.Reward.GrantedReward> rewards, CancellationToken ct)
		{
			if (rewards == null || rewards.Count == 0)
			{
				return;
			}

			_rewardPopupCts?.Cancel();
			_rewardPopupCts?.Dispose();
			_rewardPopupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(REWARD_POPUP_ADDRESS, _rewardPopupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			RewardPopup popup = go.GetComponent<RewardPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(REWARD_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(rewards, _rewardPopupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(REWARD_POPUP_ADDRESS);
			}
		}

		// 퀘스트 목록 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// 프리펩이 하나뿐이라 주소는 상수로 둔다.
		public async UniTask ShowQuestListPopupAsync(CancellationToken ct)
		{
			_questListCts?.Cancel();
			_questListCts?.Dispose();
			_questListCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(QUEST_LIST_POPUP_ADDRESS, _questListCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			QuestListPopup popup = go.GetComponent<QuestListPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(QUEST_LIST_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_questListCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(QUEST_LIST_POPUP_ADDRESS);
			}
		}

		// 지도 화면 — 메뉴 지도 버튼·미니맵 확장 버튼이 연다.
		public UniTask OpenMapScreenAsync(CancellationToken ct)
		{
			return openMenuScreenAsync<MapScreen>(MAP_SCREEN_ADDRESS, ct);
		}

		// 코스튬 화면 — 메뉴 코스튬 버튼이 연다.
		public UniTask OpenCostumeScreenAsync(CancellationToken ct)
		{
			return openMenuScreenAsync<CostumeInfoUI>(COSTUME_SCREEN_ADDRESS, ct);
		}

		// 펫 화면 — 메뉴 펫 버튼이 연다.
		public UniTask OpenPetScreenAsync(CancellationToken ct)
		{
			return openMenuScreenAsync<PetInfoUI>(PET_SCREEN_ADDRESS, ct);
		}

		// 메뉴에서 여는 전체 화면 — 창(Window) 층에 연다. 열린 창(네비게이션 탭 화면)은 먼저 닫고 탭 선택도 푼다.
		// 닫기는 화면의 X(CloseWindowAsync), 네비게이션 탭을 누르면 그 화면으로 바뀐다. 이미 맨 위에 떠 있으면 새로 열지 않는다.
		private async UniTask openMenuScreenAsync<T>(string address, CancellationToken ct) where T : UIScreen
		{
			// 여는 중(프리펩 로드 대기)에 또 누르면 두 장 뜨지 않게 막는다.
			if (_menuScreenOpening == true || (_windowStack.Count > 0 && _windowStack.Peek().screen is T))
			{
				return;
			}

			_menuScreenOpening = true;
			if (_windowStack.Count > 0)
			{
				await CloseAllWindowsAsync();
				EventManager.Instance.Publish(new WindowClosedEvent());
			}

			await OpenWindowAsync<T>(address, ct).SuppressCancellationThrow();
			_menuScreenOpening = false;
		}

		// 출석 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// 프리펩이 하나뿐이라 주소는 상수로 둔다.
		public async UniTask ShowDailyBonusPopupAsync(CancellationToken ct)
		{
			_dailyBonusCts?.Cancel();
			_dailyBonusCts?.Dispose();
			_dailyBonusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(DAILY_BONUS_POPUP_ADDRESS, _dailyBonusCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			DailyBonusPopup popup = go.GetComponent<DailyBonusPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_DailyBonusPopup 루트에 DailyBonusPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(DAILY_BONUS_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_dailyBonusCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(DAILY_BONUS_POPUP_ADDRESS);
			}
		}

		// 히어로패스 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// 프리펩이 하나뿐이라 주소는 상수로 둔다.
		public async UniTask ShowHeroPassPopupAsync(CancellationToken ct)
		{
			_heroPassCts?.Cancel();
			_heroPassCts?.Dispose();
			_heroPassCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(HERO_PASS_POPUP_ADDRESS, _heroPassCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			HeroPassPopup popup = go.GetComponent<HeroPassPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_PassPopup 루트에 HeroPassPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(HERO_PASS_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_heroPassCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(HERO_PASS_POPUP_ADDRESS);
			}
		}

		// 전투력 랭킹 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		public async UniTask ShowRankingPopupAsync(CancellationToken ct)
		{
			_rankingCts?.Cancel();
			_rankingCts?.Dispose();
			_rankingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(RANKING_POPUP_ADDRESS, _rankingCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			RankingPopup popup = go.GetComponent<RankingPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_RankingPopup 루트에 RankingPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(RANKING_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_rankingCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(RANKING_POPUP_ADDRESS);
			}
		}

		// 플레이어 정보 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다. 내 정보·남의 정보 공통.
		public async UniTask ShowPlayerInfoPopupAsync(PlayerProfile profile, CancellationToken ct)
		{
			_playerInfoCts?.Cancel();
			_playerInfoCts?.Dispose();
			_playerInfoCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(PLAYER_INFO_POPUP_ADDRESS, _playerInfoCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			PlayerInfoPopup popup = go.GetComponent<PlayerInfoPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_PlayerInfoPopup 루트에 PlayerInfoPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(PLAYER_INFO_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(profile, _playerInfoCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(PLAYER_INFO_POPUP_ADDRESS);
			}
		}

		// 일괄 분해 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// 확인 팝업(공용 팝업)은 별도 CTS 라 이 팝업 위에 떠도 이 팝업이 닫히지 않는다.
		public async UniTask ShowDecompositionPopupAsync(CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(DECOMPOSITION_POPUP_ADDRESS, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			DecompositionPopup popup = go.GetComponent<DecompositionPopup>();
			if (popup == null)
			{
				Debug.LogError("[UIManager] UIPrefab_DecompositionPopup 루트에 DecompositionPopup 이 붙어 있지 않다.");
				Destroy(go);
				ResourceManager.Instance.Release(DECOMPOSITION_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(_popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(DECOMPOSITION_POPUP_ADDRESS);
			}
		}

		// 상자 확률표. 프리펩이 하나뿐이라 주소를 호출부에서 받지 않는다.
		public async UniTask ShowBoxRewardPopupAsync(int rewardGroupId, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(BOX_REWARD_POPUP_ADDRESS, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = spawnPopup(prefab);
			BoxRewardPopup popup = go.GetComponent<BoxRewardPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(BOX_REWARD_POPUP_ADDRESS);
				return;
			}

			await popup.ShowAsync(rewardGroupId, _popupCts.Token);
			Destroy(go);

			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(BOX_REWARD_POPUP_ADDRESS);
			}
		}

		// 공용 확인 팝업을 열고 어떤 버튼이 눌렸는지 돌려준다 (UI Toolkit — UIPopupId.Common).
		public UniTask<CommonPopupResult> ShowCommonPopupAsync(CommonPopupData data, CancellationToken ct)
		{
			return ShowPopupAsync<CommonPopupToolkit, CommonPopupData, CommonPopupResult>(UIPopupId.Common, data, ct);
		}

		// ── UI Toolkit 툴팁 ─────────────────────────────────────────────
		// 누른 요소 위에 잠깐 뜨는 이름 + 설명(재화·재료처럼 정식 팝업이 필요 없는 대상). 팝업 층에 하나만 둔다.
		// 누른 요소의 가로 중앙, 바로 위에 띄우고 화면 밖으로 나가면 안쪽으로 당긴다(위로 넘치면 요소 아래로).

		private const float TooltipSeconds = 2f;
		private const float TooltipGap = 20f;		// 앵커와 툴팁 사이 여백
		private const float TooltipPadding = 16f;	// 화면 끝에서 띄울 여백

		private VisualElement _tooltip;
		private Label _tooltipTitle;
		private Label _tooltipDesc;
		private Rect _tooltipAnchor;
		private CancellationTokenSource _tooltipCts;

		// desc 가 비면 이름 한 줄만 띄운다(예: "복사 완료").
		public async UniTask ShowTooltipAsync(string title, string desc, VisualElement anchor, CancellationToken ct)
		{
			if (_tooltipCts != null)
			{
				_tooltipCts.Cancel();
				_tooltipCts.Dispose();
			}

			_tooltipCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			CancellationTokenSource mine = _tooltipCts;

			if (_tooltip == null)
			{
				_tooltip = new VisualElement();
				_tooltip.AddToClassList("tooltip");
				_tooltip.pickingMode = PickingMode.Ignore;
				_tooltip.RegisterCallback<GeometryChangedEvent>(onTooltipGeometryChanged);

				_tooltipTitle = new Label();
				_tooltipTitle.AddToClassList("tooltip__title");
				_tooltipTitle.pickingMode = PickingMode.Ignore;
				_tooltip.Add(_tooltipTitle);

				_tooltipDesc = new Label();
				_tooltipDesc.AddToClassList("tooltip__desc");
				_tooltipDesc.pickingMode = PickingMode.Ignore;
				_tooltip.Add(_tooltipDesc);
			}

			_tooltipTitle.text = title;
			_tooltipDesc.text = desc;
			_tooltipDesc.style.display = string.IsNullOrEmpty(desc) ? DisplayStyle.None : DisplayStyle.Flex;
			_tooltipAnchor = (anchor != null) ? anchor.worldBound : new Rect(540f, 960f, 0f, 0f);
			getLayerRoot(_popupLayer).Add(_tooltip);
			placeTooltip();

			bool cancelled = await UniTask.Delay(System.TimeSpan.FromSeconds(TooltipSeconds), cancellationToken: mine.Token).SuppressCancellationThrow();

			// 새 툴팁이 이어받았으면(그쪽이 다시 붙였다) 건드리지 않는다.
			if (cancelled == true && _tooltipCts != mine)
			{
				return;
			}

			_tooltip.RemoveFromHierarchy();
		}

		// 크기가 정해진 뒤 앵커 위·화면 안쪽으로 다시 맞춘다.
		private void onTooltipGeometryChanged(GeometryChangedEvent evt)
		{
			placeTooltip();
		}

		private void placeTooltip()
		{
			VisualElement layer = _tooltip.parent;
			if (layer == null)
			{
				return;
			}

			float width = _tooltip.resolvedStyle.width;
			float height = _tooltip.resolvedStyle.height;
			if (float.IsNaN(width) == true || float.IsNaN(height) == true)
			{
				return;
			}

			float maxLeft = layer.resolvedStyle.width - width - TooltipPadding;
			float left = Mathf.Clamp(_tooltipAnchor.center.x - width * 0.5f, TooltipPadding, Mathf.Max(TooltipPadding, maxLeft));

			// 위가 화면 밖이면 앵커 아래로 내리고, 그래도 아래가 넘치면 화면 안쪽으로 당긴다.
			float top = _tooltipAnchor.yMin - TooltipGap - height;
			if (top < TooltipPadding)
			{
				top = _tooltipAnchor.yMax + TooltipGap;
			}

			float maxTop = layer.resolvedStyle.height - height - TooltipPadding;
			top = Mathf.Clamp(top, TooltipPadding, Mathf.Max(TooltipPadding, maxTop));

			// 같은 값을 다시 쓰면 GeometryChangedEvent 가 또 오지 않는다 — 바뀔 때만 쓴다.
			if (Mathf.Approximately(_tooltip.resolvedStyle.left, left) == false)
			{
				_tooltip.style.left = left;
			}

			if (Mathf.Approximately(_tooltip.resolvedStyle.top, top) == false)
			{
				_tooltip.style.top = top;
			}
		}

		// ── UI Toolkit 팝업 ─────────────────────────────────────────────
		// 팝업마다 ShowXxxPopupAsync 를 두지 않고 이 함수 하나로 연다 (UI구조_설계.md 4.1).
		// 같은 ID 를 다시 열면 앞의 것을 닫고, 다른 ID 는 위에 겹친다 — 위 팝업이 아래 팝업을 취소하지 않는다.

		private readonly Dictionary<UIPopupId, CancellationTokenSource> _toolkitPopupCts = new Dictionary<UIPopupId, CancellationTokenSource>();

		public async UniTask<TResult> ShowPopupAsync<TPopup, TArgs, TResult>(UIPopupId id, TArgs args, CancellationToken ct)
			where TPopup : UIPopup<TArgs, TResult>
		{
			string address = UIPopupCatalog.GetAddress(id);
			if (string.IsNullOrEmpty(address) == true)
			{
				Debug.LogError($"[UIManager] {id} 의 주소가 UIPopupCatalog 에 없습니다.");
				return default;
			}

			CancellationTokenSource previous;
			if (_toolkitPopupCts.TryGetValue(id, out previous) == true)
			{
				// 앞의 팝업은 자기 흐름에서 닫히고 CTS 를 정리한다.
				previous.Cancel();
			}

			CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			_toolkitPopupCts[id] = cts;

			TResult result = await showPopupCoreAsync<TPopup, TArgs, TResult>(address, args, cts.Token);

			CancellationTokenSource current;
			if (_toolkitPopupCts.TryGetValue(id, out current) == true && current == cts)
			{
				_toolkitPopupCts.Remove(id);
			}

			cts.Dispose();
			return result;
		}

		private async UniTask<TResult> showPopupCoreAsync<TPopup, TArgs, TResult>(string address, TArgs args, CancellationToken ct)
			where TPopup : UIPopup<TArgs, TResult>
		{
			// 로드 중 취소돼도 ShowPopupAsync 의 CTS 정리까지 이어지게 예외 대신 결과로 받는다.
			(bool cancelled, GameObject prefab) = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct).SuppressCancellationThrow();
			if (cancelled == true || prefab == null)
			{
				return default;
			}

			GameObject go = spawnPopup(prefab);
			TPopup popup = go.GetComponent<TPopup>();
			if (popup == null)
			{
				Debug.LogError($"[UIManager] {address} 에 {typeof(TPopup).Name} 이 없습니다.");
				Destroy(go);
				releaseWindow(address);
				return default;
			}

			popup.Attach(getLayerRoot(_popupLayer));
			TResult result = await popup.ShowAsync(args, ct);

			Destroy(go);
			releaseWindow(address);
			return result;
		}

		// 캐릭터 디테일 팝업을 팝업 층(창보다 위)에 열고 닫힘을 기다린다.
		// ── 이벤트 핸들러 ───────────────────────────────────────────────

		// 상태가 전이될 때 열려있는 창을 모두 닫는다.
		// 각 State의 EnterAsync에서 필요한 창을 새로 열도록 위임.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			CloseAllWindowsAsync().Forget();
		}
	}
}
