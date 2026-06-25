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
	// UI 전역 진입점.
	// - 씬을 가로지르는 Canvas 계층(Overlay / Popup) 관리
	// - GameStateChangedEvent를 구독해 오버레이 스택을 자동 정리
	// (씬 전용 HUD는 각 씬의 Canvas에 직접 배치 — 매니저가 소유하지 않음)
	public class UIManager : MonoSingleton<UIManager>
	{
		[Header("Canvas 계층")]
		[SerializeField] private Canvas _overlayCanvas;	// Sort Order 200, DontDestroyOnLoad
		[SerializeField] private Canvas _popupCanvas;	// Sort Order 300, DontDestroyOnLoad
		[SerializeField] private Canvas _systemCanvas;	// Sort Order 400 — 네트워크 딤(최상위), DontDestroyOnLoad

		[Header("네트워크 딤")]
		[SerializeField] private GameObject _networkBlockerPrefab;

		// 이 시간(초) 안에 응답이 오면 딤을 띄우지 않는다(빠른 응답에서 화면 깜빡임 방지).
		private const float NetworkBlockerShowDelaySec = 0.2f;

		// 오버레이 스택 (Back키 처리, 직렬 닫기용)
		private readonly Stack<UIScreen> _overlayStack = new Stack<UIScreen>();

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
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
			base.OnDestroy();
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

		// ── 오버레이 ────────────────────────────────────────────────────

		// Addressable 주소로 오버레이를 열어 _overlayCanvas 아래에 배치한다.
		public async UniTask<T> OpenOverlayAsync<T>(string address, CancellationToken ct) where T : UIScreen
		{
			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				return null;
			}

			GameObject go = Instantiate(prefab, _overlayCanvas.transform);
			T screen = go.GetComponent<T>();
			if (screen == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return null;
			}

			_overlayStack.Push(screen);
			await screen.OnOpenAsync(ct);
			return screen;
		}

		// 스택 최상단 오버레이를 닫는다.
		// publishWhenEmpty: 스택이 비었을 때 OverlayClosedEvent를 발행할지.
		// 사용자 닫기(닫기 버튼)는 true(기본), 탭 전환·씬 전환의 일괄 닫기는 false로 조용히 닫는다.
		public async UniTask CloseOverlayAsync(bool publishWhenEmpty = true)
		{
			if (_overlayStack.Count == 0)
			{
				return;
			}

			UIScreen screen = _overlayStack.Pop();
			await screen.OnCloseAsync();
			Destroy(screen.gameObject);

			// 마지막 오버레이가 닫혀 스택이 비면 통지 (탭 그룹 등이 선택 해제).
			if (publishWhenEmpty && _overlayStack.Count == 0)
			{
				EventManager.Instance.Publish(new OverlayClosedEvent());
			}
		}

		// 모든 오버레이를 닫는다 (탭 전환·씬 전환 시 호출 — 조용히 닫음).
		public async UniTask CloseAllOverlaysAsync()
		{
			while (_overlayStack.Count > 0)
			{
				await CloseOverlayAsync(false);
			}
		}

		// ── 공통 팝업 ───────────────────────────────────────────────────

		// 확인/취소 팝업. 확인이면 true, 취소·외부취소면 false를 반환한다.
		public async UniTask<bool> ShowConfirmPopupAsync(string address, string message, CancellationToken ct)
		{
			_popupCts?.Cancel();
			_popupCts?.Dispose();
			_popupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, _popupCts.Token);
			if (prefab == null)
			{
				return false;
			}

			GameObject go = Instantiate(prefab, _popupCanvas.transform);
			ConfirmPopup popup = go.GetComponent<ConfirmPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return false;
			}

			bool result = await popup.WaitResultAsync(message, _popupCts.Token);
			Destroy(go);
			ResourceManager.Instance.Release(address);
			return result;
		}

		// 아이템 정보 팝업을 _popupCanvas(오버레이보다 상위)에 열고 닫힘을 기다린다.
		public async UniTask ShowItemInfoPopupAsync(string address, int itemId, CancellationToken ct)
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

			await popup.ShowAsync(itemId, _popupCts.Token);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 캐릭터 디테일 팝업을 _popupCanvas(오버레이보다 상위)에 열고 닫힘을 기다린다.
		public async UniTask ShowCharacterDetailPopupAsync(string address, int characterId, CancellationToken ct)
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
			CharacterDetailPopup popup = go.GetComponent<CharacterDetailPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(characterId, _popupCts.Token);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// 특성 스킬 디테일 팝업을 캐릭터 디테일 팝업 위에 중첩해서 연다.
		// 주의: 부모(캐릭터 디테일)를 닫지 않기 위해 _popupCts 를 건드리지 않고, 넘겨받은 ct 를 그대로 쓴다.
		// (부모가 닫히면 그 ct 가 취소되며 이 팝업도 함께 정리된다.)
		public async UniTask ShowSkillDetailPopupAsync(string address, int traitGroupId, int slotLevel, CancellationToken ct)
		{
			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				return;
			}

			GameObject go = Instantiate(prefab, _popupCanvas.transform);
			SkillDetailPopup popup = go.GetComponent<SkillDetailPopup>();
			if (popup == null)
			{
				Destroy(go);
				ResourceManager.Instance.Release(address);
				return;
			}

			await popup.ShowAsync(traitGroupId, slotLevel, ct);
			Destroy(go);

			// 종료/취소 흐름에서 ResourceManager 가 이미 파괴됐으면 Instance 는 null — 가드 후 해제
			if (ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(address);
			}
		}

		// ── 이벤트 핸들러 ───────────────────────────────────────────────

		// 상태가 전이될 때 열려있는 오버레이를 모두 닫는다.
		// 각 State의 EnterAsync에서 필요한 오버레이를 새로 열도록 위임.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			CloseAllOverlaysAsync().Forget();
		}
	}
}
