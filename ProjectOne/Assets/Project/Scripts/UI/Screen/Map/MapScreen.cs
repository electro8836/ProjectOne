using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Resources;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 지도 화면(UIPrefab_MapScreen, UI Toolkit 창). 레이아웃은 UIToolkit/Screens/Map/MapScreen.uxml.
	//
	// 미니맵 확장 버튼·HUD 지도 버튼이 UIManager.OpenMapScreenAsync 로 연다.
	// 볼 수 있는 맵: 지금 있는 곳(마을·던전이면 그 맵) + 모든 월드. ◀ ▶ 로 넘긴다.
	// 지금 있는 월드는 띄워 둔 맵을 그대로 쓰고, 다른 월드는 맵 프리팹 에셋을 불러 구워 둔 지도와 표시를 읽는다(띄우지 않는다).
	// 지도는 끌어서 움직이고 +/- 로 줌, 현재 위치 버튼은 내 캐릭터를 가운데에 맞춘다(다른 맵을 보고 있으면 지금 맵으로 돌아온다).
	// 설정 버튼은 같은 화면 위에 지도 설정 창을 띄운다 — 값은 MapSettings(기기 로컬)에 저장한다.
	// 네비게이션 바까지 덮는 전체 화면이다 — 떠 있는 동안 네비게이션 바를 숨기고, 닫기는 제목 판의 X.
	public class MapScreen : ToolkitScreen
	{
		private const float BossRefreshInterval = 1f;
		private const float ZoomStep = 1.5f;
		private const float MaxPixelsPerUnit = 24f;
		private const float HeroPixelsPerUnit = 10f;	// 지금 맵을 열 때·현재 위치를 누를 때의 줌

		private sealed class Entry
		{
			public string name;
			public MinimapImage image;
			public WorldMap prefabWorld;	// 띄우지 않은 월드 — null 이면 지금 띄워 둔 맵
			public bool current;
		}

		private static readonly MapSetting[] SettingRows =
		{
			MapSetting.MinimapEnabled,
			MapSetting.MinimapText,
			MapSetting.TeleportButton,
			MapSetting.Chest,
			MapSetting.Boss,
			MapSetting.Altar,
			MapSetting.Quest,
			MapSetting.Npc,
			MapSetting.Portal
		};

		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<MapMarker> _markers = new List<MapMarker>();
		private readonly List<Entry> _entries = new List<Entry>();
		private readonly List<string> _acquired = new List<string>();
		private readonly List<VisualElement> _dots = new List<VisualElement>();
		private readonly Button[] _settingButtons = new Button[SettingRows.Length];

		private MapView _view;
		private VisualElement _map;
		private Label _name;
		private Button _prev;
		private Button _next;
		private Button _zoomIn;
		private Button _zoomOut;
		private VisualElement _dotRoot;
		private VisualElement _settings;

		private int _selected = -1;
		private bool _laidOut;
		private UnitBase _hero;
		private IntervalTimer _bossTimer;
		private bool _dragging;
		private int _pointerId = -1;
		private Vector2 _lastPointer;

		public override bool HidesNavigationBar
		{
			get { return true; }
		}

		protected override void OnBind(VisualElement root)
		{
			_map = root.Q("map");
			_name = root.Q<Label>("map-name");
			_prev = root.Q<Button>("prev");
			_next = root.Q<Button>("next");
			_zoomIn = root.Q<Button>("zoom-in");
			_zoomOut = root.Q<Button>("zoom-out");
			_dotRoot = root.Q("dots");
			_settings = root.Q("settings");
			_view = new MapView(_map, _icons, this.GetCancellationTokenOnDestroy());
			_view.ApplySettings(false);

			root.Q<Button>("close").clicked += onCloseClicked;
			_prev.clicked += onPrevClicked;
			_next.clicked += onNextClicked;
			_zoomIn.clicked += onZoomInClicked;
			_zoomOut.clicked += onZoomOutClicked;
			root.Q<Button>("locate").clicked += onLocateClicked;
			root.Q<Button>("setting").clicked += onSettingClicked;
			root.Q<Button>("settings-close").clicked += closeSettings;
			root.Q("settings-dim").RegisterCallback<ClickEvent>(onSettingsDimClicked);

			for (int i = 0; i < SettingRows.Length; i++)
			{
				Button row = root.Q<Button>("set-" + SettingRows[i].ToString());
				row.userData = SettingRows[i];
				row.RegisterCallback<ClickEvent>(onSettingRowClicked);
				_settingButtons[i] = row;
			}

			_map.RegisterCallback<GeometryChangedEvent>(onMapGeometryChanged);
			_map.RegisterCallback<PointerDownEvent>(onPointerDown);
			_map.RegisterCallback<PointerMoveEvent>(onPointerMove);
			_map.RegisterCallback<PointerUpEvent>(onPointerUp);
			_map.RegisterCallback<PointerCaptureOutEvent>(onPointerCaptureOut);

			EventManager.Instance.Subscribe<MapSettingChangedEvent>(onMapSettingChanged);
			refreshSettingRows();
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				EventManager.Instance.Unsubscribe<MapSettingChangedEvent>(onMapSettingChanged);
			}

			_icons.ReleaseAll();
			if (ResourceManager.HasInstance == true)
			{
				for (int i = 0; i < _acquired.Count; i++)
				{
					ResourceManager.Instance.Release(_acquired[i]);
				}
			}

			_acquired.Clear();
			base.OnDestroy();
		}

		public override async UniTask OnOpenAsync(CancellationToken ct)
		{
			_hero = findHero();
			await buildEntriesAsync(ct);
			buildDots();

			int start = 0;
			for (int i = 0; i < _entries.Count; i++)
			{
				if (_entries[i].current == true)
				{
					start = i;
					break;
				}
			}

			select(start);
		}

		private void Update()
		{
			if (IsBound == false || _selected < 0 || _view.HasMap == false)
			{
				return;
			}

			if (_entries[_selected].current == true && _hero != null && _hero.gameObject.activeInHierarchy == true)
			{
				_view.SetHero(_hero.CachedPos);
			}

			if (_bossTimer.Tick(Time.deltaTime, BossRefreshInterval) > 0 && MapMarkerCollector.RefreshBosses(_markers) == true)
			{
				_view.RefreshTexts(_markers);
			}
		}

		// ── 맵 목록 ────────────────────────────────────────────────────

		// 지금 있는 곳이 월드가 아니면(마을·던전) 그 맵을 맨 앞에 두고, 뒤에 모든 월드를 ID 순으로 둔다.
		private async UniTask buildEntriesAsync(CancellationToken ct)
		{
			_entries.Clear();
			int currentWorldId = 0;
			if (ProjectOne.Field.WorldDirector.HasInstance == true)
			{
				currentWorldId = ProjectOne.Field.WorldDirector.Instance.CurrentWorldId;
			}

			MinimapImage currentImage = MapMarkerCollector.FindCurrentImage();
			if (currentWorldId == 0 && currentImage != null)
			{
				Entry here = new Entry();
				here.name = buildPlaceName();
				here.image = currentImage;
				here.current = true;
				_entries.Add(here);
			}

			List<Table_World.Row> worlds = new List<Table_World.Row>(Table_World.All().Values);
			worlds.Sort(compareWorldId);
			for (int i = 0; i < worlds.Count; i++)
			{
				Table_World.Row row = worlds[i];
				Entry entry = new Entry();
				entry.name = row.Name;
				if (row.ID == currentWorldId)
				{
					entry.image = currentImage;
					entry.current = true;
				}
				else
				{
					entry.prefabWorld = await loadWorldAsync(row.MapPrefab, ct);
					entry.image = MapMarkerCollector.FindImage(entry.prefabWorld);
				}

				if (entry.image != null)
				{
					_entries.Add(entry);
				}
			}
		}

		private static int compareWorldId(Table_World.Row a, Table_World.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		// 월드 맵 프리팹 에셋 — 띄우지 않고 컴포넌트만 읽는다. 화면이 닫힐 때 놓는다.
		private async UniTask<WorldMap> loadWorldAsync(string address, CancellationToken ct)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return null;
			}

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(address, ct);
			if (prefab == null)
			{
				Debug.LogWarning($"[MapScreen] 월드 맵 프리팹을 찾지 못했습니다: {address}");
				return null;
			}

			_acquired.Add(address);
			return prefab.GetComponent<WorldMap>();
		}

		private void buildDots()
		{
			_dotRoot.Clear();
			_dots.Clear();
			for (int i = 0; i < _entries.Count; i++)
			{
				VisualElement dot = new VisualElement();
				dot.AddToClassList("map-select__dot");
				dot.pickingMode = PickingMode.Ignore;
				_dotRoot.Add(dot);
				_dots.Add(dot);
			}
		}

		private void select(int index)
		{
			if (index < 0 || index >= _entries.Count)
			{
				_name.text = "지도";
				_prev.SetEnabled(false);
				_next.SetEnabled(false);
				return;
			}

			_selected = index;
			Entry entry = _entries[index];
			_name.text = entry.name;
			_prev.SetEnabled(index > 0);
			_next.SetEnabled(index < _entries.Count - 1);
			for (int i = 0; i < _dots.Count; i++)
			{
				_dots[i].EnableInClassList("map-select__dot--on", i == index);
			}

			_view.SetMap(entry.image);
			if (entry.prefabWorld != null)
			{
				MapMarkerCollector.Collect(entry.prefabWorld, _markers);
			}
			else
			{
				MapMarkerCollector.Collect(entry.image, _markers);
			}

			_view.SetMarkers(_markers);

			bool showHero = entry.current == true && _hero != null;
			_view.SetHeroVisible(showHero);
			if (showHero == true)
			{
				_view.SetHero(_hero.CachedPos);
				_view.SetZoom(Mathf.Max(HeroPixelsPerUnit, _view.FitPixelsPerUnit));
				_view.SetCenter(clampCenter(_hero.CachedPos));
			}
			else
			{
				_view.SetZoom(_view.FitPixelsPerUnit);
				_view.SetCenter(_view.WorldCenter);
			}

			refreshZoomButtons();
		}

		// 처음 배치가 끝나야 화면 크기를 안다 — 그 전에 고른 맵의 줌·중심을 다시 맞춘다.
		private void onMapGeometryChanged(GeometryChangedEvent evt)
		{
			if (_laidOut == true || evt.newRect.width <= 0f || _selected < 0)
			{
				return;
			}

			_laidOut = true;
			select(_selected);
		}

		// ── 줌·이동 ────────────────────────────────────────────────────

		private void zoom(float factor)
		{
			float fit = _view.FitPixelsPerUnit;
			float next = Mathf.Clamp(_view.PixelsPerUnit * factor, fit, Mathf.Max(fit, MaxPixelsPerUnit));
			_view.SetZoom(next);
			_view.SetCenter(clampCenter(_view.Center));
			refreshZoomButtons();
		}

		private void refreshZoomButtons()
		{
			float fit = _view.FitPixelsPerUnit;
			_zoomIn.SetEnabled(_view.PixelsPerUnit < Mathf.Max(fit, MaxPixelsPerUnit) - 0.01f);
			_zoomOut.SetEnabled(_view.PixelsPerUnit > fit + 0.01f);
		}

		// 지도 밖으로 끌려 나가지 않게 — 화면 가운데가 지도 범위 안에 머문다.
		private Vector2 clampCenter(Vector2 center)
		{
			Vector2 half = _view.ViewportSize * 0.5f / _view.PixelsPerUnit;
			Vector2 min = _view.WorldCenter - _view.WorldSize * 0.5f;
			Vector2 max = _view.WorldCenter + _view.WorldSize * 0.5f;
			float x = clampAxis(center.x, min.x + half.x, max.x - half.x);
			float y = clampAxis(center.y, min.y + half.y, max.y - half.y);
			return new Vector2(x, y);
		}

		// 지도가 화면보다 작은 축은 가운데에 고정한다.
		private static float clampAxis(float value, float low, float high)
		{
			if (low > high)
			{
				return (low + high) * 0.5f;
			}

			return Mathf.Clamp(value, low, high);
		}

		private void onPointerDown(PointerDownEvent evt)
		{
			if (_view.HasMap == false)
			{
				return;
			}

			_dragging = true;
			_pointerId = evt.pointerId;
			_lastPointer = evt.position;
			_map.CapturePointer(evt.pointerId);
		}

		// 손가락이 움직인 만큼 지도를 같은 방향으로 민다 — 보는 중심은 반대로 간다. UI 는 y 가 아래로 자란다.
		private void onPointerMove(PointerMoveEvent evt)
		{
			if (_dragging == false || evt.pointerId != _pointerId)
			{
				return;
			}

			Vector2 pos = evt.position;
			Vector2 delta = pos - _lastPointer;
			_lastPointer = pos;
			float ppu = _view.PixelsPerUnit;
			Vector2 center = _view.Center + new Vector2(-delta.x / ppu, delta.y / ppu);
			_view.SetCenter(clampCenter(center));
		}

		private void onPointerUp(PointerUpEvent evt)
		{
			if (evt.pointerId == _pointerId && _map.HasPointerCapture(evt.pointerId) == true)
			{
				_map.ReleasePointer(evt.pointerId);
			}

			_dragging = false;
		}

		private void onPointerCaptureOut(PointerCaptureOutEvent evt)
		{
			_dragging = false;
		}

		// ── 버튼 ───────────────────────────────────────────────────────

		private void onCloseClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		private void onPrevClicked()
		{
			select(_selected - 1);
		}

		private void onNextClicked()
		{
			select(_selected + 1);
		}

		private void onZoomInClicked()
		{
			zoom(ZoomStep);
		}

		private void onZoomOutClicked()
		{
			zoom(1f / ZoomStep);
		}

		// 내 캐릭터를 가운데에 — 다른 맵을 보고 있으면 지금 맵으로 돌아온다.
		private void onLocateClicked()
		{
			for (int i = 0; i < _entries.Count; i++)
			{
				if (_entries[i].current == true)
				{
					if (i != _selected)
					{
						select(i);
						return;
					}

					if (_hero != null)
					{
						_view.SetCenter(clampCenter(_hero.CachedPos));
					}

					return;
				}
			}
		}

		// ── 설정 ───────────────────────────────────────────────────────

		private void onSettingClicked()
		{
			_settings.AddToClassList("map-settings--open");
		}

		private void closeSettings()
		{
			_settings.RemoveFromClassList("map-settings--open");
		}

		private void onSettingsDimClicked(ClickEvent evt)
		{
			closeSettings();
		}

		private void onSettingRowClicked(ClickEvent evt)
		{
			MapSetting setting = (MapSetting)((VisualElement)evt.currentTarget).userData;
			MapSettings.Set(setting, MapSettings.Get(setting) == false);
		}

		private void onMapSettingChanged(MapSettingChangedEvent e)
		{
			_view.ApplySettings(false);
			refreshSettingRows();
		}

		private void refreshSettingRows()
		{
			for (int i = 0; i < SettingRows.Length; i++)
			{
				_settingButtons[i].EnableInClassList("map-set-row--off", MapSettings.Get(SettingRows[i]) == false);
			}
		}

		// ── 도움 ───────────────────────────────────────────────────────

		// 월드가 아닌 지금 있는 곳의 이름 — 던전이면 던전 이름, 마을이면 마을 이름.
		private static string buildPlaceName()
		{
			if (ProjectOne.Dungeon.DungeonDirector.HasInstance == true && ProjectOne.Dungeon.DungeonDirector.Instance.DungeonType != EDT.Dungeon.None)
			{
				Table_Dungeon.Row dungeon = Table_Dungeon.Get(ProjectOne.Dungeon.DungeonDirector.Instance.DungeonType);
				if (dungeon != null)
				{
					return dungeon.Name;
				}
			}

			if (ProjectOne.Town.TownDirector.HasInstance == true)
			{
				Table_Map.Row town = Table_Map.Get(ProjectOne.Town.TownDirector.Instance.TownMapId);
				if (town != null)
				{
					return town.Name;
				}
			}

			return "지도";
		}

		private static UnitBase findHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			return heroes.Count > 0 ? heroes[0] : null;
		}
	}
}
