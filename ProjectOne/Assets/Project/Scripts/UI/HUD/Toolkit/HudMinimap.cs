using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Unit;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 메인 HUD 미니맵 — 오른쪽 위 메뉴 버튼 아래의 원형 지도.
	//
	// 히어로를 가운데에 두고 지도가 따라 움직인다. +/- 로 줌, 확장 버튼으로 월드맵 창을 연다.
	// 지도·표시는 월드맵 창과 같은 MapView·MapMarkerCollector 를 쓴다.
	// 맵이 바뀌었는지는 알림이 없어 지금 맵(MapManager.Current)의 지도 이미지 참조를 매 프레임 비교한다(참조 비교 1회).
	// 지도 이미지가 없는 맵(아직 굽지 않음)에서는 칸째 숨긴다.
	public sealed class HudMinimap
	{
		// 줌 단계 — 월드 1 유닛당 픽셀. 가운데가 기본값.
		private static readonly float[] ZoomLevels = { 6f, 10f, 16f };
		private const int DefaultZoomIndex = 1;

		// 필드보스 남은 시간 글자 갱신 간격
		private const float BossRefreshInterval = 1f;

		private readonly VisualElement _slot;
		private readonly Button _zoomIn;
		private readonly Button _zoomOut;
		private readonly MapView _view;
		private readonly CancellationToken _ct;
		private readonly List<MapMarker> _markers = new List<MapMarker>();

		private MinimapImage _image;
		private bool _visible;
		private int _zoomIndex = DefaultZoomIndex;
		private UnitBase _hero;
		private IntervalTimer _bossTimer;

		// 표시를 모을 때의 완료 퀘스트 — NPC 등장 조건이 바뀌었는지 비교한다.
		private int _clearedQuestId;

		public HudMinimap(VisualElement root, ToolkitIconLoader icons, CancellationToken ct)
		{
			_slot = root.Q("minimap");
			_zoomIn = root.Q<Button>("minimap-zoom-in");
			_zoomOut = root.Q<Button>("minimap-zoom-out");
			_ct = ct;
			_view = new MapView(root.Q("minimap-view"), icons, ct);

			_zoomIn.clicked += onZoomInClicked;
			_zoomOut.clicked += onZoomOutClicked;
			root.Q<Button>("minimap-expand").clicked += onExpandClicked;

			EventManager.Instance.Subscribe<QuestChangeEvent>(onQuestChanged);

			applyZoom();
			setVisible(false);
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<QuestChangeEvent>(onQuestChanged);
		}

		public void Tick(float deltaTime)
		{
			MinimapImage image = MapMarkerCollector.FindCurrentImage();
			if (image != _image)
			{
				_image = image;
				_hero = null;
				rebuild();
			}

			if (_visible == false)
			{
				return;
			}

			UnitBase hero = findHero();
			if (hero != null)
			{
				_view.SetHero(hero.CachedPos);
				_view.SetCenter(hero.CachedPos);
			}

			if (_bossTimer.Tick(deltaTime, BossRefreshInterval) > 0 && MapMarkerCollector.RefreshBosses(_markers) == true)
			{
				_view.RefreshTexts(_markers);
			}
		}

		private void rebuild()
		{
			_view.SetMap(_image);
			setVisible(_view.HasMap);
			if (_view.HasMap == false)
			{
				return;
			}

			MapMarkerCollector.Collect(_image, _markers);
			_view.SetMarkers(_markers);
			_clearedQuestId = (Account.Instance.Quests != null) ? Account.Instance.Quests.ClearedQuestId : 0;
		}

		private void setVisible(bool visible)
		{
			_visible = visible;
			_slot.EnableInClassList("minimap--shown", visible);
		}

		// 히어로는 씬이 바뀌면 새로 만들어진다 — 들고 있던 것이 죽었거나 사라졌으면 다시 찾는다.
		private UnitBase findHero()
		{
			if (_hero != null && _hero.gameObject.activeInHierarchy == true)
			{
				return _hero;
			}

			_hero = null;
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			if (heroes.Count > 0)
			{
				_hero = heroes[0];
			}

			return _hero;
		}

		private void applyZoom()
		{
			_view.SetZoom(ZoomLevels[_zoomIndex]);
			_zoomIn.SetEnabled(_zoomIndex < ZoomLevels.Length - 1);
			_zoomOut.SetEnabled(_zoomIndex > 0);
		}

		// ── 입력·이벤트 ───────────────────────────────────────────────

		private void onZoomInClicked()
		{
			if (_zoomIndex < ZoomLevels.Length - 1)
			{
				_zoomIndex++;
				applyZoom();
			}
		}

		private void onZoomOutClicked()
		{
			if (_zoomIndex > 0)
			{
				_zoomIndex--;
				applyZoom();
			}
		}

		private void onExpandClicked()
		{
			UIManager.Instance.ShowWorldMapPopupAsync(_ct).Forget();
		}

		// NPC 등장 조건은 완료한 퀘스트로 정해진다. 진행도 갱신마다 이벤트가 오므로 완료 퀘스트가 바뀔 때만 다시 모은다.
		private void onQuestChanged(QuestChangeEvent e)
		{
			if (_visible == false || Account.Instance.Quests == null)
			{
				return;
			}

			int cleared = Account.Instance.Quests.ClearedQuestId;
			if (cleared == _clearedQuestId)
			{
				return;
			}

			rebuild();
		}
	}
}
