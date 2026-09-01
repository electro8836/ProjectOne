using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Event;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// UI 전역 진입점. 씬을 가로지르는 Canvas 계층을 소유한다.
	//
	//   Overlay(100)  상시 HUD          — MainHUD
	//   Window (200)  전체창            — 장비 / 상점 / 던전선택
	//   Navi   (300)  창 위 상시        — 네비게이션 바
	//   Popup  (350)  네비게이션 위     — 아이템 정보 · 확인
	//   System (400)  최상위            — 네트워크 딤 · 로딩
	//
	// 위로 갈수록 덮는다. HUD 를 가장 아래에 두어 창·팝업이 자연히 그 위에 뜬다.
	// GameStateChangedEvent 를 구독해 열린 창을 자동 정리한다.
	public class UIManager : MonoSingleton<UIManager>
	{
		[Header("Canvas 계층")]
		[SerializeField] private Canvas _hudCanvas;	// Sort Order 100 — 상시 HUD
		[SerializeField] private Canvas _windowCanvas;	// Sort Order 200 — 전체창
		[SerializeField] private Canvas _popupCanvas;	// Sort Order 350 — 네비게이션 위 작은 창
		[SerializeField] private Canvas _navigationCanvas;	// Sort Order 300 — 네비게이션 바(창 위 상시)
		[SerializeField] private Canvas _systemCanvas;	// Sort Order 400 — 네트워크 딤(최상위)

		// WorldSpace — 유닛 머리 위에 뜨는 게이지들. 위 5개와 달리 화면이 아니라 월드에 그려진다.
		// 중첩 Canvas 는 루트의 renderMode 를 따르므로 반드시 다른 캔버스의 자식이 아니라
		// UIManager 직속(=Canvas 없는 루트의 자식)이어야 WorldSpace 로 산다.
		[SerializeField] private Canvas _worldCanvas;

		[Header("네트워크 딤")]
		[SerializeField] private GameObject _networkBlockerPrefab;

		// 이 시간(초) 안에 응답이 오면 딤을 띄우지 않는다(빠른 응답에서 화면 깜빡임 방지).
		private const float NetworkBlockerShowDelaySec = 0.2f;

		// 열린 창 스택 (Back키 처리, 직렬 닫기용)
		private readonly Stack<UIScreen> _windowStack = new Stack<UIScreen>();

		// 씬에 직접 배치된 HUD 등 화면 레지스트리 — 깨어날 때 등록/사라질 때 해제하여 타입으로 O(1) 조회
		private readonly Dictionary<System.Type, UIScreen> _screens = new Dictionary<System.Type, UIScreen>();

		// 현재 진행 중인 팝업의 CancellationTokenSource
		private CancellationTokenSource _popupCts;

		// 네트워크 딤 — 1회 생성 후 캐시(SetActive 토글로 재사용)
		private GameObject _networkBlocker;
		// 동시 네트워크 요청 참조카운트 — 0이 되면 딤을 닫는다.
		private int _blockerRefCount;
		// 지연 표시 코루틴 — 닫힘 시 중지
		private Coroutine _blockerDelayCo;

		protected override void Awake()
		{
			base.Awake();
			ensureEventSystem();
			validateCanvases();
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

		// 캔버스 배선이 끊기면 그 캔버스를 쓰는 모든 경로가 NRE 로 죽는다.
		// 증상은 "UI 가 안 뜬다" 로만 보여서 프리팹부터 뒤지게 되므로, 어느 것이 비었는지 여기서 밝힌다.
		//
		// 특히 직렬화 필드를 개명하면 유니티가 이름을 키로 쓰기 때문에 프리팹의 값이 조용히 떨어져 나간다.
		// 그 사고를 부팅 첫 줄에서 잡는 것이 목적이다.
		private void validateCanvases()
		{
			if (_hudCanvas == null)
			{
				Debug.LogError("[UIManager] _hudCanvas 가 비었습니다 — MainHUD 를 띄울 수 없습니다.");
			}

			if (_windowCanvas == null)
			{
				Debug.LogError("[UIManager] _windowCanvas 가 비었습니다 — 창을 열 수 없습니다.");
			}

			if (_popupCanvas == null)
			{
				Debug.LogError("[UIManager] _popupCanvas 가 비었습니다 — 팝업을 열 수 없습니다.");
			}

			if (_navigationCanvas == null)
			{
				Debug.LogError("[UIManager] _navigationCanvas 가 비었습니다 — 네비게이션 바를 띄울 수 없습니다.");
			}

			if (_systemCanvas == null)
			{
				Debug.LogError("[UIManager] _systemCanvas 가 비었습니다 — 네트워크 딤을 띄울 수 없습니다.");
			}

			if (_worldCanvas == null)
			{
				Debug.LogError("[UIManager] _worldCanvas 가 비었습니다 — 월드 게이지를 띄울 수 없습니다.");
			}
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
		// 참조카운트로 동시/연속 요청을 견디고, 지연 시간 내 응답이 오면 딤을 띄우지 않는다.

		// 요청 시작 — 참조카운트를 올리고 첫 요청이면 지연 표시를 예약한다.
		public void ShowNetworkBlocker()
		{
			_blockerRefCount++;
			if (_blockerRefCount > 1)
			{
				return;	// 이미 표시(또는 지연 대기) 중
			}

			// 0→1: 지연 표시 시작. 직전 대기가 남아있지 않게 정리 후 재시작.
			if (_blockerDelayCo != null)
			{
				StopCoroutine(_blockerDelayCo);
			}

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

			if (_networkBlocker != null)
			{
				_networkBlocker.SetActive(false);
			}
		}

		// 지연 후 딤 표시 — 지연 동안 모든 요청이 끝나면(refCount 0) 표시하지 않는다.
		private IEnumerator showBlockerDelayed()
		{
			yield return new WaitForSeconds(NetworkBlockerShowDelaySec);
			_blockerDelayCo = null;

			if (_blockerRefCount <= 0)
			{
				yield break;
			}

			// 딤은 최초 1회만 생성해 캐시하고, 이후 SetActive 로 재사용한다.
			if (_networkBlocker == null)
			{
				Transform parent = (_systemCanvas != null) ? _systemCanvas.transform : _popupCanvas.transform;
				_networkBlocker = Instantiate(_networkBlockerPrefab, parent);
			}

			_networkBlocker.SetActive(true);
		}

		// ── 영속 MainHUD ────────────────────────────────────────────────
		//
		// 씬별 HUD 를 쓰지 않는다. 하나가 씬을 가로질러 살아 있고, 어디서 무엇을 보일지는
		// HudContext 가시성으로 정한다 (MainHud.ApplyContext).

		private const string MainHudAddress = "UIPrefab_MainHUD";

		private GameObject _mainHud;

		// 마을 진입 시 1회. 이미 떠 있으면 아무것도 하지 않는다.
		//
		// **히어로 스폰보다 먼저 불려야 한다.** JoystickController 가 Awake 에서 UnitSpawnedEvent 를
		// 구독하므로, 히어로가 뜬 뒤에 HUD 가 생기면 이벤트를 놓쳐 조이스틱이 영영 대상을 못 찾는다.
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

			// HUD 캔버스(100) 자식으로 둔다 — 창(200)·네비(300)·팝업(350)·시스템(400)이 자연히 위를 덮는다.
			// UIManager 자신이 영속이라 별도 DontDestroyOnLoad 가 필요 없다.
			_mainHud = Instantiate(prefab, _hudCanvas.transform);
		}

		// ── 영속 네비게이션 바 ──────────────────────────────────────────
		//
		// MainHUD 와 같은 영속 UI 지만 캔버스가 다르다 — 창(200) 위에 상시 떠야 하므로
		// 전용 Navigation(300) 캔버스에 붙인다. 어느 맥락에서 보일지는 NavigationBar 가 스스로 정한다.

		private const string NavigationBarAddress = "UIPrefab_NavigationBar";

		private GameObject _navigationBar;

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

			_navigationBar = Instantiate(prefab, _navigationCanvas.transform);
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

		// Addressable 주소로 창을 열어 _windowCanvas 아래에 배치한다.
		public async UniTask<T> OpenWindowAsync<T>(string address, CancellationToken ct) where T : UIScreen
		{
			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				return null;
			}

			GameObject go = Instantiate(prefab, _windowCanvas.transform);
			T screen = go.GetComponent<T>();
			if (screen == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return null;
			}

			_windowStack.Push(screen);
			await screen.OnOpenAsync(ct);
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

			UIScreen screen = _windowStack.Pop();
			await screen.OnCloseAsync();
			Destroy(screen.gameObject);

			// 마지막 창이 닫혀 스택이 비면 통지 (탭 그룹 등이 선택 해제).
			if (publishWhenEmpty && _windowStack.Count == 0)
			{
				EventManager.Instance.Publish(new WindowClosedEvent());
			}
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

		// 아이템 정보 팝업을 _popupCanvas(창보다 상위)에 열고 닫힘을 기다린다.
		public async UniTask ShowItemInfoPopupAsync(string address, long uid, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = Instantiate(prefab, _popupCanvas.transform);
			ItemInfoPopup popup = go.GetComponent<ItemInfoPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(uid, _popupCts.Token);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 소모품 정보 팝업을 _popupCanvas(창보다 상위)에 열고 닫힘을 기다린다.
		// 소모품은 스택이라 장비처럼 인스턴스 UID 가 없어 아이템 ID 로 연다.
		public async UniTask ShowConsumablePopupAsync(string address, int itemId, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return;
			}

			GameObject go = Instantiate(prefab, _popupCanvas.transform);
			ConsumableInfoPopup popup = go.GetComponent<ConsumableInfoPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(itemId, _popupCts.Token);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 캐릭터 디테일 팝업을 _popupCanvas(오버레이보다 상위)에 열고 닫힘을 기다린다.
		// ── 이벤트 핸들러 ───────────────────────────────────────────────

		// 상태가 전이될 때 열려있는 창을 모두 닫는다.
		// 각 State의 EnterAsync에서 필요한 창을 새로 열도록 위임.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			CloseAllWindowsAsync().Forget();
		}
	}
}
