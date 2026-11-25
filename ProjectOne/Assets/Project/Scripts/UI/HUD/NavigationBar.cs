using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 하단 네비게이션 바(UIPrefab_NavigationBar, UI Toolkit). 레이아웃은 UIToolkit/HUD/Navigation/NavigationBar.uxml.
	// UIManager 가 Navigation 층(300)에 한 번 세워 두고, 보일 맥락(마을·필드)과 창이 가리는지에 따라 스스로 숨는다.
	//
	// 탭을 누르면 그 화면으로 창을 교체하고, 열린 탭을 다시 누르면 창을 모두 닫는다.
	// 월드 탭은 화면이 아직 없어 "준비 중" 알림만 띄운다.
	public class NavigationBar : ToolkitScreen
	{
		private const int TAB_COUNT = 5;
		private const string ComingSoon = "준비 중입니다";

		// 탭 순서와 열 화면 — UXML 의 tab-0 ~ tab-4 와 같은 순서다.
		private static readonly UIScreenId[] TabScreens =
		{
			UIScreenId.Shop,
			UIScreenId.Equipment,
			UIScreenId.World,
			UIScreenId.Mastery,
			UIScreenId.Craft
		};

		[SerializeField] private HudContext _visibleContexts = HudContext.Town | HudContext.Field;

		private readonly Button[] _tabs = new Button[TAB_COUNT];
		private VisualElement _nav;

		private int _openedIndex = -1;
		private bool _isSwitching;
		private bool _contextVisible;
		private bool _hiddenByWindow;

		protected override void OnBind(VisualElement root)
		{
			_nav = root.Q("nav");
			for (int i = 0; i < TAB_COUNT; i++)
			{
				Button tab = root.Q<Button>("tab-" + i);
				tab.userData = i;
				tab.RegisterCallback<ClickEvent>(onTabClicked);
				_tabs[i] = tab;
			}

			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
			EventManager.Instance.Subscribe<WindowClosedEvent>(onWindowClosed);
			applyVisible();
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
				EventManager.Instance.Unsubscribe<WindowClosedEvent>(onWindowClosed);
			}

			base.OnDestroy();
		}

		// 창 스택 최상단이 네비게이션 바를 가리는지 (UIManager 가 알려 준다).
		public void SetHiddenByWindow(bool hidden)
		{
			_hiddenByWindow = hidden;
			applyVisible();
		}

		// 코드에서 탭을 연다 — 탭을 누른 것과 같이 창을 교체하고 선택 표시도 옮긴다.
		public UniTask<UIScreen> OpenTabAsync(UIScreenId screen)
		{
			if (_isSwitching == true)
			{
				return UniTask.FromResult<UIScreen>(null);
			}

			int index = indexOf(screen);
			if (index < 0)
			{
				Debug.Log($"[NavigationBar] {screen} 에 연결된 탭이 없습니다.");
				return UniTask.FromResult<UIScreen>(null);
			}

			select(index);
			return switchScreenAsync(screen, index);
		}

		// ── 내부 ─────────────────────────────────────────────────────────

		private void applyVisible()
		{
			if (_nav != null)
			{
				_nav.style.display = (_contextVisible == true && _hiddenByWindow == false) ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		private void select(int index)
		{
			for (int i = 0; i < TAB_COUNT; i++)
			{
				_tabs[i].EnableInClassList("nav-tab--selected", i == index);
			}
		}

		private static int indexOf(UIScreenId screen)
		{
			for (int i = 0; i < TabScreens.Length; i++)
			{
				if (TabScreens[i] == screen)
				{
					return i;
				}
			}

			return -1;
		}

		private void onTabClicked(ClickEvent evt)
		{
			if (_isSwitching == true)
			{
				return;
			}

			int index = (int)((VisualElement)evt.currentTarget).userData;
			if (TabScreens[index] == UIScreenId.World)
			{
				UIManager.Instance.ShowAlertMessage(ComingSoon);
				return;
			}

			if (index == _openedIndex)
			{
				closeAsync().Forget();
				return;
			}

			select(index);
			switchScreenAsync(TabScreens[index], index).Forget();
		}

		// 실패하면 선택 표시를 이전 상태로 되돌린다.
		private async UniTask<UIScreen> switchScreenAsync(UIScreenId screen, int index)
		{
			_isSwitching = true;
			UIScreen opened = await UIManager.Instance.SwitchWindowAsync(screen, this.GetCancellationTokenOnDestroy());
			if (opened == null)
			{
				select(_openedIndex);
				_isSwitching = false;
				return null;
			}

			_openedIndex = index;
			_isSwitching = false;
			return opened;
		}

		private async UniTaskVoid closeAsync()
		{
			_isSwitching = true;
			await UIManager.Instance.CloseAllWindowsAsync();
			_openedIndex = -1;
			select(-1);
			_isSwitching = false;
		}

		// 창이 닫기 버튼으로 모두 닫히면 선택을 푼다.
		private void onWindowClosed(WindowClosedEvent e)
		{
			_openedIndex = -1;
			select(-1);
		}

		private void onGameStateChanged(GameStateChangedEvent e)
		{
			HudContext context = HudContexts.FromState(e.StateType);
			_contextVisible = (_visibleContexts & context) != 0;
			_hiddenByWindow = false;
			applyVisible();

			_openedIndex = -1;
			select(-1);
		}
	}
}
