using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Map;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 월드맵 팝업(UIPrefab_WorldMapPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/WorldMap/WorldMapPopup.uxml.
	//
	// 미니맵 확장 버튼이 연다. 지금 있는 곳의 지도 전체를 창에 맞춰 보여 준다 —
	// 필드면 현재 월드, 마을이면 마을 지도, 던전이면 그 던전 지도. 표시는 미니맵과 같다. 보기 전용이다.
	public class WorldMapPopup : ToolkitScreen
	{
		private const float BossRefreshInterval = 1f;

		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<MapMarker> _markers = new List<MapMarker>();

		private MapView _view;
		private VisualElement _map;
		private Label _title;
		private UniTaskCompletionSource _tcs;
		private UnitBase _hero;
		private IntervalTimer _bossTimer;

		protected override void OnBind(VisualElement root)
		{
			_title = root.Q<Label>("title");
			_map = root.Q("map");
			_view = new MapView(_map, _icons, this.GetCancellationTokenOnDestroy());

			// 창 크기가 정해진 뒤에야 지도를 맞출 줌을 안다.
			_map.RegisterCallback<GeometryChangedEvent>(onMapGeometryChanged);

			root.Q<Button>("close").clicked += Close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
		}

		protected override void OnDestroy()
		{
			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			MinimapImage image = MapMarkerCollector.FindCurrentImage();
			_title.text = buildTitle();
			_view.SetMap(image);
			MapMarkerCollector.Collect(image, _markers);
			_view.SetMarkers(_markers);
			_hero = findHero();
			_view.SetHeroVisible(_hero != null);
			fit();
			PlayPopupOpen();

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		private void Update()
		{
			if (IsBound == false || _view.HasMap == false)
			{
				return;
			}

			if (_hero != null && _hero.gameObject.activeInHierarchy == true)
			{
				_view.SetHero(_hero.CachedPos);
			}

			if (_bossTimer.Tick(Time.deltaTime, BossRefreshInterval) > 0 && MapMarkerCollector.RefreshBosses(_markers) == true)
			{
				_view.RefreshTexts(_markers);
			}
		}

		private void fit()
		{
			_view.SetZoom(_view.FitPixelsPerUnit);
			_view.SetCenter(_view.WorldCenter);
		}

		private void onMapGeometryChanged(GeometryChangedEvent evt)
		{
			fit();
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}

		// 제목 — 필드면 월드 이름, 마을이면 마을 이름, 던전이면 던전 이름.
		private static string buildTitle()
		{
			if (ProjectOne.Field.WorldDirector.HasInstance == true)
			{
				Table_World.Row world = Table_World.Get(ProjectOne.Field.WorldDirector.Instance.CurrentWorldId);
				if (world != null)
				{
					return world.Name;
				}
			}

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
