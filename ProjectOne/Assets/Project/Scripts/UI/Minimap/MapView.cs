using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Map;

namespace ProjectOne.UI
{
	// 지도 한 장 + 표시(마커) + 히어로 위치 — HUD 미니맵과 월드맵 창이 같이 쓴다.
	//
	// 뷰포트(잘라 보이는 창, USS 의 overflow: hidden) 안에 지도 크기만 한 판(content)을 두고
	// 판의 크기(줌)와 위치(중심)만 바꾼다. 마커는 판 위의 비율 좌표라 판을 따라 움직이지만
	// 아이콘·글자 크기는 줌과 상관없이 화면에서 같다.
	//
	// 레이아웃·스타일 쓰기는 값이 바뀔 때만 한다 — 미니맵은 매 프레임 Follow 를 부른다.
	public sealed class MapView
	{
		// 이 픽셀 이상 움직여야 판 위치를 다시 쓴다.
		private const float MoveEpsilon = 0.25f;

		private readonly VisualElement _viewport;
		private readonly VisualElement _content;
		private readonly VisualElement _hero;
		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ct;
		private readonly List<MarkerElement> _markers = new List<MarkerElement>();

		private Vector2 _worldMin;
		private Vector2 _worldSize;
		private bool _hasMap;

		private float _pixelsPerUnit = 10f;
		private Vector2 _center;
		private Vector2 _shownOffset = new Vector2(float.NaN, float.NaN);
		private Vector2 _shownSize = new Vector2(float.NaN, float.NaN);
		private Vector2 _heroPos = new Vector2(float.NaN, float.NaN);

		private sealed class MarkerElement
		{
			public VisualElement root;
			public VisualElement icon;
			public Label label;
			public string iconName;
		}

		public MapView(VisualElement viewport, ToolkitIconLoader icons, CancellationToken ct)
		{
			_viewport = viewport;
			_icons = icons;
			_ct = ct;

			_content = new VisualElement();
			_content.AddToClassList("map-view__content");
			_content.pickingMode = PickingMode.Ignore;
			_viewport.Add(_content);

			_hero = new VisualElement();
			_hero.AddToClassList("map-view__hero");
			_hero.pickingMode = PickingMode.Ignore;
			_content.Add(_hero);

			// 처음 배치가 끝나야 뷰포트 크기를 안다.
			_viewport.RegisterCallback<GeometryChangedEvent>(onGeometryChanged);
		}

		public bool HasMap
		{
			get { return _hasMap; }
		}

		public Vector2 ViewportSize
		{
			get { return new Vector2(_viewport.resolvedStyle.width, _viewport.resolvedStyle.height); }
		}

		// 지도 크기에 맞춰 뷰포트를 꽉 채우는 줌 (월드맵 창)
		public float FitPixelsPerUnit
		{
			get
			{
				Vector2 view = ViewportSize;
				if (_hasMap == false || float.IsNaN(view.x) == true || _worldSize.x <= 0f || _worldSize.y <= 0f)
				{
					return _pixelsPerUnit;
				}

				return Mathf.Min(view.x / _worldSize.x, view.y / _worldSize.y);
			}
		}

		public Vector2 WorldCenter
		{
			get { return _worldMin + _worldSize * 0.5f; }
		}

		public Vector2 WorldSize
		{
			get { return _worldSize; }
		}

		public float PixelsPerUnit
		{
			get { return _pixelsPerUnit; }
		}

		public Vector2 Center
		{
			get { return _center; }
		}

		// 지도 설정을 적용한다 — 꺼진 종류의 아이콘은 뷰포트 클래스로 숨긴다(MapView.uss).
		// applyText: 미니맵만 "미니맵 텍스트 표시" 설정을 따른다.
		public void ApplySettings(bool applyText)
		{
			_viewport.EnableInClassList("map-view--no-text", applyText == true && MapSettings.Get(MapSetting.MinimapText) == false);
			_viewport.EnableInClassList("map-view--hide-boss", MapSettings.Get(MapSetting.Boss) == false);
			_viewport.EnableInClassList("map-view--hide-npc", MapSettings.Get(MapSetting.Npc) == false);
			_viewport.EnableInClassList("map-view--hide-portal", MapSettings.Get(MapSetting.Portal) == false);
		}

		public void SetMap(MinimapImage image)
		{
			_hasMap = image != null && image.Texture != null;
			if (_hasMap == false)
			{
				_content.style.backgroundImage = new StyleBackground(StyleKeyword.None);
				return;
			}

			_worldMin = image.WorldMin;
			_worldSize = image.WorldMax - image.WorldMin;
			_content.style.backgroundImage = new StyleBackground(image.Texture);
			_heroPos = new Vector2(float.NaN, float.NaN);
			invalidate();
		}

		public void SetZoom(float pixelsPerUnit)
		{
			_pixelsPerUnit = pixelsPerUnit;
			apply();
		}

		// 화면 가운데에 둘 월드 좌표
		public void SetCenter(Vector2 worldPos)
		{
			_center = worldPos;
			apply();
		}

		public void SetHero(Vector2 worldPos)
		{
			if (_hasMap == false || (worldPos - _heroPos).sqrMagnitude < 0.0001f)
			{
				return;
			}

			_heroPos = worldPos;
			placeOnContent(_hero, worldPos);
		}

		public void SetHeroVisible(bool visible)
		{
			_hero.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 표시 목록을 통째로 다시 그린다 — 맵이 바뀌거나 NPC 등장 조건이 바뀔 때.
		public void SetMarkers(IReadOnlyList<MapMarker> markers)
		{
			for (int i = 0; i < markers.Count; i++)
			{
				MarkerElement element = getOrCreateMarker(i);
				element.root.style.display = StyleKeyword.Null;	// 인라인을 비워야 USS 의 종류별 숨김(지도 설정)이 먹는다
				placeOnContent(element.root, markers[i].Position);
				if (element.iconName != markers[i].Icon)
				{
					element.iconName = markers[i].Icon;
					_icons.SetAsync(element.icon, markers[i].Icon, _ct).Forget();
				}

				applyText(element, markers[i]);
			}

			for (int i = markers.Count; i < _markers.Count; i++)
			{
				_markers[i].root.style.display = DisplayStyle.None;
			}

			// 히어로가 마커 위에 그려지게 맨 뒤로 보낸다.
			_hero.BringToFront();
		}

		// 글자·생사만 다시 쓴다 — 필드보스 남은 시간이 1초마다 바뀐다.
		public void RefreshTexts(IReadOnlyList<MapMarker> markers)
		{
			int count = Mathf.Min(markers.Count, _markers.Count);
			for (int i = 0; i < count; i++)
			{
				applyText(_markers[i], markers[i]);
			}
		}

		private static void applyText(MarkerElement element, MapMarker marker)
		{
			string text = marker.Label != null ? marker.Label : string.Empty;
			if (element.label.text != text)
			{
				element.label.text = text;
			}

			element.label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
			element.root.EnableInClassList("map-marker--dead", marker.Dead);
			element.root.EnableInClassList("map-marker--boss", marker.Kind == MapMarkerKind.Boss);
			element.root.EnableInClassList("map-marker--npc", marker.Kind == MapMarkerKind.Npc);
			element.root.EnableInClassList("map-marker--portal", marker.Kind == MapMarkerKind.Gate);
		}

		private MarkerElement getOrCreateMarker(int index)
		{
			if (index < _markers.Count)
			{
				return _markers[index];
			}

			MarkerElement element = new MarkerElement();
			element.root = new VisualElement();
			element.root.AddToClassList("map-marker");
			element.root.pickingMode = PickingMode.Ignore;

			element.icon = new VisualElement();
			element.icon.AddToClassList("map-marker__icon");
			element.icon.pickingMode = PickingMode.Ignore;
			element.root.Add(element.icon);

			element.label = new Label();
			element.label.AddToClassList("map-marker__label");
			element.label.pickingMode = PickingMode.Ignore;
			element.root.Add(element.label);

			_content.Add(element.root);
			_markers.Add(element);
			return element;
		}

		// 판 위 비율 좌표 — 판 크기(줌)가 바뀌어도 다시 쓸 필요가 없다. UI 는 y 가 아래로 자란다.
		private void placeOnContent(VisualElement element, Vector2 worldPos)
		{
			if (_worldSize.x <= 0f || _worldSize.y <= 0f)
			{
				return;
			}

			float x = (worldPos.x - _worldMin.x) / _worldSize.x * 100f;
			float y = (1f - (worldPos.y - _worldMin.y) / _worldSize.y) * 100f;
			element.style.left = Length.Percent(x);
			element.style.top = Length.Percent(y);
		}

		private void onGeometryChanged(GeometryChangedEvent evt)
		{
			invalidate();
		}

		private void invalidate()
		{
			_shownOffset = new Vector2(float.NaN, float.NaN);
			_shownSize = new Vector2(float.NaN, float.NaN);
			apply();
		}

		private void apply()
		{
			Vector2 view = ViewportSize;
			if (_hasMap == false || float.IsNaN(view.x) == true || float.IsNaN(view.y) == true)
			{
				return;
			}

			Vector2 size = _worldSize * _pixelsPerUnit;
			if (Mathf.Abs(size.x - _shownSize.x) > MoveEpsilon || Mathf.Abs(size.y - _shownSize.y) > MoveEpsilon || float.IsNaN(_shownSize.x) == true)
			{
				_shownSize = size;
				_content.style.width = size.x;
				_content.style.height = size.y;
			}

			// 중심 좌표가 뷰포트 가운데에 오게 판을 민다.
			float left = view.x * 0.5f - (_center.x - _worldMin.x) * _pixelsPerUnit;
			float top = view.y * 0.5f - (_worldMin.y + _worldSize.y - _center.y) * _pixelsPerUnit;
			if (Mathf.Abs(left - _shownOffset.x) > MoveEpsilon || Mathf.Abs(top - _shownOffset.y) > MoveEpsilon || float.IsNaN(_shownOffset.x) == true)
			{
				_shownOffset = new Vector2(left, top);
				_content.style.left = left;
				_content.style.top = top;
			}
		}
	}
}
