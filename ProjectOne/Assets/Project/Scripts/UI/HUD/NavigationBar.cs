using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 화면 하단 상시 네비게이션 바.
	//
	// MainHUD 하단에 있던 탭 묶음을 떼어낸 것이다. 뗀 이유는 레이어다 —
	// MainHUD 는 Overlay(100) 라 창(200)·팝업(300)에 가려지지만, 네비게이션은 창 위에 상시 떠야 한다.
	// 그래서 UIManager 의 Navigation(350) 캔버스에 따로 붙는다.
	//
	// Presenter 를 두지 않는다. 하는 일이 "탭 인덱스 → UIScreenId → 창 열기" 뿐이라 나눌 상태가 없다.
	public class NavigationBar : UIScreen
	{
		// 배타 선택은 TabGroup 이 알아서 하고, 여기서는 "선택되면 무엇을 여는가"만 정한다.
		[SerializeField] private TabGroup _tabs;

		// 탭 순서(Hierarchy 순서)대로의 화면. None 이거나 배열이 짧으면 로그만 남기고 넘어간다.
		[SerializeField] private UIScreenId[] _tabScreens;

		// 이 맥락에서만 보인다. 던전처럼 네비게이션이 방해가 되는 곳에서는 체크를 뺀다.
		[SerializeField] private HudContext _visibleContexts = HudContext.Town | HudContext.Field;

		// 현재 열려 있는 화면의 탭 인덱스. 열린 화면이 없으면 -1.
		// 같은 탭을 다시 눌렀는지 판별하는 유일한 근거다 — 선택 표시와 어긋나면 토글이 통째로 틀어진다.
		private int _openedIndex = -1;

		// 창을 여닫는 동안 들어온 탭 입력은 버린다.
		// 화면 열기가 await(Addressable 로드) 라, 연타하면 _openedIndex 가 갱신되기 전에
		// 다음 클릭이 들어와 "같은 탭인가" 판정이 어긋난다.
		private bool _isSwitching;

		private void Awake()
		{
			if (_tabs != null)
			{
				_tabs.OnTabChanged += onTabChanged;
			}

			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Subscribe<WindowClosedEvent>(onWindowClosed);

			// 부트 직후에는 어느 맥락도 아니다 — 숨긴 상태로 시작하고 상태 전이가 켠다.
			// 이 컴포넌트는 EventManager(순수 C#) 로 구독하므로 비활성 상태에서도 통지를 받는다.
			gameObject.SetActive(false);
		}

		private void OnDestroy()
		{
			if (_tabs != null)
			{
				_tabs.OnTabChanged -= onTabChanged;
			}

			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Unsubscribe<WindowClosedEvent>(onWindowClosed);
		}

		// ── 탭 ────────────────────────────────────────────────────────

		private void onTabChanged(int index)
		{
			if (_isSwitching == true)
			{
				return;
			}

			// 이미 열려 있는 탭을 다시 눌렀다 — 연다가 아니라 닫는다.
			//
			// TabGroup 은 범용 배타 선택 컴포넌트라 재클릭을 구분하지 않는다(EquipmentUI 의 분류 탭처럼
			// 선택이 풀리면 안 되는 사용처가 있다). 그래서 판별은 여기서 한다.
			if (index == _openedIndex)
			{
				closeAsync().Forget();
				return;
			}

			if (_tabScreens == null || index < 0 || index >= _tabScreens.Length)
			{
				Debug.Log($"[NavigationBar] 탭 {index} 에 연결된 화면이 없습니다.");
				return;
			}

			UIScreenId screen = _tabScreens[index];
			if (screen == UIScreenId.None)
			{
				Debug.Log($"[NavigationBar] 탭 {index} 에 연결된 화면이 없습니다.");
				return;
			}

			switchScreenAsync(screen, index).Forget();
		}

		// 탭 전환은 교체지 겹치기가 아니다 — 먼저 열린 창을 모두 닫지 않으면
		// 탭을 옮길 때마다 UIManager 의 창 스택에 쌓인다.
		// 닫기는 조용히(false) 한다. WindowClosedEvent 가 발행되면 방금 누른 탭이 곧바로 해제된다.
		private async UniTaskVoid switchScreenAsync(UIScreenId screen, int index)
		{
			_isSwitching = true;

			await UIManager.Instance.CloseAllWindowsAsync();
			UIScreen opened = await UIManager.Instance.OpenAsync(screen, this.GetCancellationTokenOnDestroy());

			// 열기에 실패하면(주소 누락·프리팹 없음) 창이 뜨지 않은 것이다.
			// 선택 표시만 남겨두면 "선택돼 있는데 화면은 없는" 탭이 되어 재클릭이 닫기로 빠진다.
			if (opened == null)
			{
				_openedIndex = -1;
				_tabs.ClearSelection();
				_isSwitching = false;
				return;
			}

			_openedIndex = index;
			_isSwitching = false;
		}

		// 열려 있는 화면을 접는다 — 같은 탭 재클릭.
		// 조용히 닫으므로 WindowClosedEvent 가 오지 않는다. 선택 해제는 여기서 직접 한다.
		private async UniTaskVoid closeAsync()
		{
			_isSwitching = true;

			await UIManager.Instance.CloseAllWindowsAsync();

			_openedIndex = -1;
			_tabs.ClearSelection();
			_isSwitching = false;
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 사용자가 창을 직접 닫아 스택이 비면 선택 표시를 지운다 (열린 화면이 없는 상태와 일치시킨다).
		private void onWindowClosed(WindowClosedEvent e)
		{
			_openedIndex = -1;

			if (_tabs != null)
			{
				_tabs.ClearSelection();
			}
		}

		// 상태 전이가 곧 맥락 전환이다.
		//
		// 선택은 무조건 지운다 — UIManager 가 상태 전이마다 열린 창을 모두 조용히 닫으므로
		// (publishWhenEmpty=false 라 WindowClosedEvent 도 오지 않는다) 그대로 두면
		// 마을→필드처럼 네비게이션이 계속 보이는 전이에서 "열린 화면 없는 선택 탭"이 남는다.
		//
		// **활성화가 먼저다.** 이 오브젝트는 부트 직후 꺼진 채로 시작하는데, 유니티는 꺼진
		// 오브젝트의 Awake 를 미뤄 두므로 그때까지 자식 TabGroup 은 초기화되지 않은 상태다.
		// 순서를 뒤집으면 첫 마을 진입에서 TabGroup 이 죽은 채 불려 예외가 난다.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			HudContext context = HudContexts.FromState(e.StateType);
			gameObject.SetActive((_visibleContexts & context) != 0);

			_openedIndex = -1;

			if (_tabs != null)
			{
				_tabs.ClearSelection();
			}
		}
	}
}
