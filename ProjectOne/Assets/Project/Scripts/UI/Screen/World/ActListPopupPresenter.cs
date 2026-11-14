using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Field;
using ProjectOne.Map;

namespace ProjectOne.UI
{
	// 월드·구역 목록 팝업 Presenter — 어느 월드를 보여줄지와 구역 해금 판정을 담당한다.
	//
	// 처음 열 때는 지금 있는 월드를 편다. 마을이라면 서 있는 구역이 없으므로 마지막으로 연 월드를
	// 대신 편다 (ZoneProgress.GetCurrentWorldId 가 그 판단을 소유한다).
	public sealed class ActListPopupPresenter : Presenter<ActListPopup>
	{
		// 월드 정렬 버퍼 — 탭 순서를 ID 로 고정해야 해서 필요하다.
		private readonly List<Table_World.Row> _worlds = new List<Table_World.Row>();

		// 구역 렌더 버퍼 — 월드 하나의 구역 수만큼 매번 새로 담는다.
		private readonly List<Table_Zone.Row> _zones = new List<Table_Zone.Row>();
		private readonly List<FieldSlotData> _slotData = new List<FieldSlotData>();

		private int _currentTab;

		protected override void OnInitialize()
		{
			view.OnTabSelected += onTabSelected;
			view.OnMoveRequested += onMoveRequested;
			view.OnTownRequested += onTownRequested;
		}

		protected override void OnDispose()
		{
			view.OnTabSelected -= onTabSelected;
			view.OnMoveRequested -= onMoveRequested;
			view.OnTownRequested -= onTownRequested;
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			buildWorlds();
			view.SetTabCount(_worlds.Count);

			// 마을 귀환 버튼은 월드에 서 있을 때만 쓸모가 있다.
			view.SetTownButtonVisible(WorldDirector.HasInstance);

			_currentTab = indexOfWorld(ZoneProgress.GetCurrentWorldId());
			view.SelectTab(_currentTab);

			render();

			// 처음 열 때만 기준 구역 칸으로 스크롤한다 — 탭을 바꿀 때는 사용자가 고른 월드를 그대로 둔다.
			int slotIndex = indexOfZone(ZoneProgress.GetFocusZoneId());
			if (slotIndex >= 0)
			{
				view.ScrollToSlot(slotIndex);
			}

			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabSelected(int index)
		{
			_currentTab = index;
			render();
		}

		// 이동은 목적지 ID 하나로 끝난다 — 어느 상태로 갈지는 MapNavigator 가 Map 테이블을 보고 정한다.
		//
		// 이동이 시작되면 팝업뿐 아니라 월드 창도 남아 있을 이유가 없다. 다만 창을 닫는 것은
		// 팝업의 일이 아니므로(팝업은 자기를 띄운 화면을 모른다) 이동했다는 사실만 결과로 넘긴다.
		private void onMoveRequested(int zoneId)
		{
			// 잠긴 구역은 버튼이 숨겨지지만, 판정이 한곳이라 여기서도 막아 우회 경로를 없앤다.
			if (ZoneProgress.IsOpen(Table_Zone.Get(zoneId)) == false)
			{
				return;
			}

			MapNavigator.MoveToMap(zoneId, view.GetDestroyToken());
			view.CloseByMove();
		}

		// 마을 귀환도 이동이다 — 구역 슬롯 이동과 같이 월드 창까지 닫는다.
		private void onTownRequested()
		{
			MapNavigator.MoveToTown();
			view.CloseByMove();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 월드 전종을 ID 순으로 담는다 — ID 가 곧 진행 순서다.
		// Dictionary 는 순서를 보장하지 않아 정렬하지 않으면 탭 위치가 흔들린다.
		private void buildWorlds()
		{
			_worlds.Clear();

			Dictionary<int, Table_World.Row> all = Table_World.All();
			Dictionary<int, Table_World.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				_worlds.Add(e.Current.Value);
			}

			_worlds.Sort(compareWorldId);
		}

		private void render()
		{
			if (_currentTab < 0 || _currentTab >= _worlds.Count)
			{
				return;
			}

			Table_World.Row world = _worlds[_currentTab];
			view.SetActName(world.Name);

			buildSlotData(world.ID);
			view.RenderFields(_slotData);
		}

		private void buildSlotData(int worldId)
		{
			ZoneProgress.CollectZones(worldId, _zones);
			int currentZoneId = ZoneProgress.GetCurrentZoneId();

			_slotData.Clear();
			for (int i = 0; i < _zones.Count; i++)
			{
				Table_Zone.Row zone = _zones[i];

				FieldSlotData data;
				data.fieldId = zone.ID;
				data.order = zone.Order;
				data.name = zone.Name;
				data.cleared = ZoneProgress.IsOpen(zone);
				data.current = zone.ID == currentZoneId;
				_slotData.Add(data);
			}
		}

		// 월드 ID 로 탭 인덱스를 찾는다. 없으면 첫 탭 — 데이터가 비정상이어도 빈 화면을 보이지 않는다.
		private int indexOfWorld(int worldId)
		{
			for (int i = 0; i < _worlds.Count; i++)
			{
				if (_worlds[i].ID == worldId)
				{
					return i;
				}
			}

			return 0;
		}

		// 지금 렌더된 구역 목록에서의 칸 인덱스. 다른 월드의 구역이면 -1.
		private int indexOfZone(int zoneId)
		{
			for (int i = 0; i < _zones.Count; i++)
			{
				if (_zones[i].ID == zoneId)
				{
					return i;
				}
			}

			return -1;
		}

		private int compareWorldId(Table_World.Row a, Table_World.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
