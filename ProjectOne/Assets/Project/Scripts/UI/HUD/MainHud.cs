using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 화면 맥락 — 어디에 있느냐에 따라 MainHUD 의 어떤 부분이 보이는가.
	//
	// [Flags] 인 이유는 "마을과 필드에서만 보이는 버튼" 같은 조합을 인스펙터 체크박스로
	// 표현하기 위해서다. 그래야 코드가 버튼 목록을 알 필요가 없다.
	[Flags]
	public enum HudContext
	{
		None = 0,
		Town = 1 << 0,
		Field = 1 << 1,
		Dungeon = 1 << 2,
		Raid = 1 << 3
	}

	// 게임 상태 → 화면 맥락.
	//
	// MainHud 와 NavigationBar 가 같은 기준으로 보이고 숨어야 한다.
	// 각자 갖고 있으면 상태를 하나 추가할 때 한쪽만 고쳐져 조용히 어긋난다.
	public static class HudContexts
	{
		public static HudContext FromState(System.Type stateType)
		{
			if (stateType == typeof(Flow.TownState))
			{
				return HudContext.Town;
			}

			if (stateType == typeof(Flow.WorldState))
			{
				return HudContext.Field;
			}

			if (stateType == typeof(Flow.DungeonState))
			{
				return HudContext.Dungeon;
			}

			// 타이틀·패치·로딩 등 — HUD 를 보일 자리가 아니다.
			return HudContext.None;
		}
	}

	// 게임 전역 HUD 의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/HUD/MainHud/MainHud.uxml.
	//
	// **씬별 HUD 를 두지 않는다.** 이 하나가 씬을 가로질러 HUD 층(Layer)에 붙어 있고, 어디서 무엇을 보일지는
	// ContextGroup 가시성으로 정한다. 조이스틱은 UGUI 라 따로 떨어진 프리펩(UIPrefab_Joystick)이다.
	//
	// 메뉴·맥락은 Presenter 가 지시하고(Passive View), 히어로 정보·버프·보스 배너처럼 자기 이벤트를 직접 받는
	// 위젯은 Hud* 클래스로 나눠 이 화면이 만들고 갱신(Tick)·정리한다.
	public class MainHud : ToolkitScreen, IView
	{
		// 맥락별로 보일 요소 묶음 — 어떤 요소가 어디서 보이는지를 인스펙터가 소유한다.
		[Serializable]
		public class ContextGroup
		{
			[Tooltip("이 묶음이 보일 맥락(복수 선택 가능)")]
			public HudContext contexts;

			[Tooltip("UXML 요소 이름(name)")]
			public string[] elementNames;

			public ContextGroup()
			{
			}

			public ContextGroup(HudContext contexts, string elementName)
			{
				this.contexts = contexts;
				this.elementNames = new string[] { elementName };
			}
		}

		[Header("맥락별 표시 묶음")]
		[SerializeField] private ContextGroup[] _contextGroups =
		{
			new ContextGroup(HudContext.Field | HudContext.Dungeon, "map-info")
		};

		// 메뉴 팝업 요청 — 어떻게 여는지는 Presenter 가 정한다.
		public event Action OnMenuRequested;

		private readonly MainHudPresenter _presenter = new MainHudPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private VisualElement _root;
		private VisualElement _menuBadge;

		private HudResourceBar _resourceBar;
		private HudHeroInfo _heroInfo;
		private HudMapInfo _mapInfo;
		private HudQuestInfo _questInfo;
		private HudBuffList _buffList;
		private HudBossBanner _bossBanner;
		private HudUltimateButton _ultimateButton;
		private HudMinimap _minimap;

		// 보스 배너 — 던전 HUD 가 자리를 비켜줄지 판단할 때 UIManager 를 거쳐 읽는다.
		public HudBossBanner BossBanner
		{
			get { return _bossBanner; }
		}

		protected override void OnBind(VisualElement root)
		{
			_root = root;
			_menuBadge = root.Q("menu-badge");
			root.Q<Button>("menu-button").clicked += onMenuButtonClicked;

			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			_resourceBar = new HudResourceBar(root, _icons, ct);
			_heroInfo = new HudHeroInfo(root);
			_mapInfo = new HudMapInfo(root);
			_questInfo = new HudQuestInfo(root, ct);
			_buffList = new HudBuffList(root, _icons, ct);
			_bossBanner = new HudBossBanner(root);
			_ultimateButton = new HudUltimateButton(root, _icons, ct);
			_minimap = new HudMinimap(root, _icons, ct);

			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_presenter.Dispose();
				_resourceBar.Dispose();
				_heroInfo.Dispose();
				_mapInfo.Dispose();
				_questInfo.Dispose();
				_buffList.Dispose();
				_bossBanner.Dispose();
				_ultimateButton.Dispose();
				_minimap.Dispose();
				_icons.ReleaseAll();
			}

			base.OnDestroy();
		}

		private void Update()
		{
			if (IsBound == false)
			{
				return;
			}

			float deltaTime = Time.deltaTime;
			_heroInfo.Tick();
			_buffList.Tick(deltaTime);
			_bossBanner.Tick();
			_ultimateButton.Tick(deltaTime);
			_minimap.Tick(deltaTime);
		}

		// ── 표시 (Presenter 가 지시) ───────────────────────────────────

		public void SetMenuBadge(bool visible)
		{
			_menuBadge.EnableInClassList("hud-badge--on", visible);
		}

		// 맥락에 맞지 않는 묶음을 숨긴다.
		public void ApplyContext(HudContext context)
		{
			if (_contextGroups == null || _root == null)
			{
				return;
			}

			for (int i = 0; i < _contextGroups.Length; i++)
			{
				ContextGroup group = _contextGroups[i];
				if (group == null || group.elementNames == null)
				{
					continue;
				}

				DisplayStyle display = ((group.contexts & context) != 0) ? DisplayStyle.Flex : DisplayStyle.None;
				for (int n = 0; n < group.elementNames.Length; n++)
				{
					VisualElement element = _root.Q(group.elementNames[n]);
					if (element == null)
					{
						Debug.LogError($"[MainHud] 맥락 묶음의 요소를 찾지 못했습니다: {group.elementNames[n]}");
						continue;
					}

					element.style.display = display;
				}
			}
		}

		// ── 입력 ──────────────────────────────────────────────────────

		private void onMenuButtonClicked()
		{
			if (OnMenuRequested != null)
			{
				OnMenuRequested.Invoke();
			}
		}
	}
}
