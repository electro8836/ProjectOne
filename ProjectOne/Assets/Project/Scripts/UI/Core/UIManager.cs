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
	// - Canvas 계층(Scene / Overlay / Popup) 관리
	// - GameStateChangedEvent를 구독해 오버레이 스택을 자동 정리
	// - 씬이 자신의 Canvas를 등록/해제하면 Scene 층을 갱신
	public class UIManager : MonoSingleton<UIManager>
	{
		[Header("Canvas 계층")]
		[SerializeField] private Canvas _overlayCanvas;	// Sort Order 200, DontDestroyOnLoad
		[SerializeField] private Canvas _popupCanvas;	// Sort Order 300, DontDestroyOnLoad

		// Scene Canvas — 씬이 RegisterSceneCanvas로 등록
		private Canvas _sceneCanvas;

		// 오버레이 스택 (Back키 처리, 직렬 닫기용)
		private readonly Stack<UIScreen> _overlayStack = new Stack<UIScreen>();

		// 현재 진행 중인 팝업의 CancellationTokenSource
		private CancellationTokenSource _popupCts;

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

		// ── Scene Canvas 등록/해제 ───────────────────────────────────────

		// 씬의 Canvas가 Awake에서 자신을 등록한다.
		public void RegisterSceneCanvas(Canvas canvas)
		{
			_sceneCanvas = canvas;
		}

		// 씬의 Canvas가 OnDestroy에서 등록을 해제한다.
		public void UnregisterSceneCanvas(Canvas canvas)
		{
			if (_sceneCanvas == canvas)
			{
				_sceneCanvas = null;
			}
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
		public async UniTask CloseOverlayAsync()
		{
			if (_overlayStack.Count == 0)
			{
				return;
			}

			UIScreen screen = _overlayStack.Pop();
			await screen.OnCloseAsync();
			Destroy(screen.gameObject);
		}

		// 모든 오버레이를 닫는다 (씬 전환 시 호출).
		public async UniTask CloseAllOverlaysAsync()
		{
			while (_overlayStack.Count > 0)
			{
				await CloseOverlayAsync();
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

		// ── 이벤트 핸들러 ───────────────────────────────────────────────

		// 상태가 전이될 때 열려있는 오버레이를 모두 닫는다.
		// 각 State의 EnterAsync에서 필요한 오버레이를 새로 열도록 위임.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			CloseAllOverlaysAsync().Forget();
		}
	}
}
