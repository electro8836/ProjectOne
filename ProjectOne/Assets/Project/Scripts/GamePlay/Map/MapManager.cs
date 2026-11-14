using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Event;
using ProjectOne.Resources;

namespace ProjectOne.Map
{
	// 맵 로드/수명/플로우필드 질의를 전담하는 전투 수명 매니저.
	//
	// 월드는 씬에 직접 배치된 이어진 맵 하나다 (RegisterWorldMap). 구역 단위 질의는 그 맵의 ZoneArea 가 답한다.
	// 마을·던전은 맵 프리팹 하나를 Addressable 로 띄운다 (LoadMapAsync).
	//
	// 플로우필드 계산(좌표/통행/BFS)만 담당 — "누구를 향해 베이크할지"는 호출자(전투 규칙)가 정한다.
	public class MapManager : MonoSingleton<MapManager>
	{
		// 상시 매니저 — 마을·필드·던전이 모두 이 매니저를 거쳐 맵을 띄운다.
		// 맵 로드와 청소의 유일한 주체이므로 씬과 함께 죽으면 안 된다.
		protected override bool Persistent => true;

		private sealed class Entry
		{
			public int mapId;
			public GameObject instance;
			public TilemapGrid grid;
			public Vector3 origin;
			public bool sceneOwned;		// 월드 씬에 배치된 맵 — Addressable 인스턴스가 아니므로 해제하지 않는다
		}

		// Map.ID → 로드된 그리드맵
		private readonly Dictionary<int, Entry> _byMapId = new Dictionary<int, Entry>();

		// 좌표 질의가 매번 딕셔너리를 순회하지 않도록 리스트로도 들고 있는다.
		private readonly List<Entry> _ordered = new List<Entry>(8);

		// 마지막으로 질의된 그리드 — 히어로는 대개 한 그리드 안에 머무르므로 캐시가 거의 항상 적중한다.
		private Entry _lastHit;

		// 씬에 배치된 컨테이너. 없으면 ensureContainerRoot 가 **활성 씬에** 만든다(매니저 자식이 아니다).
		private MapContainer _container;

		// 월드 씬의 맵. 월드에 있을 때만 있다 — 구역 단위 질의가 이것을 본다.
		private WorldMap _world;

		public bool HasMap => _ordered.Count > 0;

		// 현재 히어로가 있는(=마지막으로 질의된) 그리드. 없으면 첫 번째.
		public TilemapGrid Current
		{
			get
			{
				if (_lastHit != null)
				{
					return _lastHit.grid;
				}

				return _ordered.Count > 0 ? _ordered[0].grid : null;
			}
		}

		// ── 로드 / 언로드 ─────────────────────────────────────────────

		// 단일 맵 로드 — 던전이 쓴다. 기존 맵은 전부 언로드한다.
		public async UniTask<bool> LoadMapAsync(int mapId, CancellationToken ct = default)
		{
			UnloadAll();
			return await loadOneAsync(mapId, Vector3.zero, ct);
		}

		// 주소로 직접 로드 — 테이블을 거치지 않는 개발/테스트 경로.
		public async UniTask<bool> LoadMapByAddressAsync(string address, CancellationToken ct = default)
		{
			UnloadAll();
			return await instantiateAsync(address, Vector3.zero, 0, ct);
		}

		// 월드 맵 등록 — 월드 씬에 직접 배치된 맵을 쓴다. 로드도 해제도 씬이 맡는다.
		// 구역 질의(스폰·NPC·시작 지점·좌표→구역)는 이후 _world 의 ZoneArea 로 답한다.
		public bool RegisterWorldMap(WorldMap world)
		{
			UnloadAll();

			if (world == null || world.Grid == null)
			{
				Debug.LogError("[MapManager] WorldMap 또는 TilemapGrid 가 없습니다 — 월드 씬 구성을 확인하세요.");
				return false;
			}

			world.Grid.InitializeFlowField();

			Entry entry = new Entry();
			entry.mapId = 0;
			entry.instance = world.gameObject;
			entry.grid = world.Grid;
			entry.origin = world.transform.position;
			entry.sceneOwned = true;

			_ordered.Add(entry);
			_world = world;
			return true;
		}

		private async UniTask<bool> loadOneAsync(int mapId, Vector3 origin, CancellationToken ct)
		{
			Table_Map.Row map = Table_Map.Get(mapId);
			if (map == null)
			{
				Debug.LogError($"[MapManager] Table_Map.Get({mapId}) == null");
				return false;
			}

			// 그리드맵 프리팹 Addressable 주소.
			string address = map.MapPrefab;
			if (string.IsNullOrEmpty(address) == true)
			{
				Debug.LogWarning($"[MapManager] Map {mapId} 의 프리팹 주소가 비어 있습니다 — 건너뜁니다.");
				return false;
			}

			return await instantiateAsync(address, origin, mapId, ct);
		}

		private async UniTask<bool> instantiateAsync(string address, Vector3 origin, int mapId, CancellationToken ct)
		{
			// Try 계열을 쓴다 — InstantiateAsync 는 미등록 주소에서 예외를 던지는데, 아래 null 분기가
			// 그걸 기대하고 있어 경고 경로가 죽는다. 맵 프리팹 미등록은 콘텐츠 제작 중 흔한 상태라
			// 여기서 흐름 전체가 죽으면 로딩 화면이 영구 고착된다.
			// 컨테이너 자식으로 띄운다 — 씬에 배치된 컨테이너면 씬 전환이 곧 정리가 된다.
			GameObject mapGo = await AddressableHelper.TryInstantiateAsync(address, ensureContainerRoot(), true, ct);
			if (mapGo == null)
			{
				Debug.LogWarning($"[MapManager] 그리드맵 프리팹을 찾지 못했습니다: {address}");
				return false;
			}

			TilemapGrid grid = mapGo.GetComponent<TilemapGrid>();
			if (grid == null)
			{
				Debug.LogError($"[MapManager] 맵 프리팹에 TilemapGrid 없음: {address}");
				AddressableHelper.ReleaseInstance(mapGo);
				return false;
			}

			// 배치 후에 플로우필드를 초기화해야 셀 좌표가 최종 위치 기준으로 계산된다.
			mapGo.transform.position = origin;
			grid.InitializeFlowField();

			Entry entry = new Entry();
			entry.mapId = mapId;
			entry.instance = mapGo;
			entry.grid = grid;
			entry.origin = origin;

			if (mapId > 0)
			{
				_byMapId[mapId] = entry;
			}

			_ordered.Add(entry);
			return true;
		}

		protected override void Awake()
		{
			base.Awake();
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
		}

		// 씬의 MapContainer 가 Awake 에서 자기를 등록한다.
		//
		// 폴백은 디렉터가 요청할 때(=씬의 Awake 가 전부 끝난 뒤) 만들어지므로
		// "폴백이 먼저 있고 나중에 씬 것이 등록되는" 경우는 생기지 않는다. 핸도버 처리가 필요 없다.
		public void RegisterContainer(MapContainer container)
		{
			if (container == null || _container == container)
			{
				return;
			}

			if (_container != null)
			{
				Debug.LogWarning($"[MapManager] MapContainer 가 둘 이상입니다 — 뒤에 등록된 {container.name} 을 씁니다.");
			}

			_container = container;
		}

		// 컨테이너가 사라졌다는 것은 그 아래 맵도 사라졌다는 뜻이다.
		//
		// 씬 언로드가 컨테이너와 맵을 함께 파괴하는데, 매니저는 영속이라 Addressable 인스턴스 핸들을
		// 아무도 해제해 주지 않는다. OnDestroy 는 씬 언로드에서 반드시 호출되므로 여기가 유일하게
		// 확실한 시점이다. 자식이 이미 파괴됐어도 ReleaseInstance 가 추적 핸들로 해제한다.
		public void UnregisterContainer(MapContainer container)
		{
			if (_container != container)
			{
				return;
			}

			_container = null;
			UnloadAll();
		}

		// 컨테이너가 없으면 **활성 씬에** 하나 만든다.
		//
		// 매니저 자식으로 달면 안 된다 — 매니저는 영속이라 컨테이너까지 씬 전환을 넘어 살아남고,
		// "컨테이너가 씬에 있으면 씬 전환이 곧 정리다" 라는 분리의 목적이 무너진다.
		private Transform ensureContainerRoot()
		{
			if (_container != null)
			{
				return _container.transform;
			}

			GameObject go = new GameObject("MapContainer (auto)");
			_container = go.AddComponent<MapContainer>();
			return _container.transform;
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);

			// 영속이라 씬 파괴가 대신 치워 주지 않는다. Addressable 인스턴스 핸들을 직접 해제한다.
			UnloadAll();
			base.OnDestroy();
		}

		// 맵이 필요 없는 상태로 나가면 비운다.
		//
		// 씬 수명이던 시절에는 씬 파괴가 맵을 함께 지웠다. 영속이 된 뒤로는 타이틀 같은 곳에서도
		// 맵이 계속 렌더되므로 여기서 끊어야 한다.
		// 맵 교체(마을↔필드↔던전)는 Load*Async 가 진입부에서 UnloadAll 을 부르므로 여기서 관여하지 않는다.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			if (usesMap(e.StateType) == true)
			{
				return;
			}

			UnloadAll();
		}

		private static bool usesMap(System.Type stateType)
		{
			return stateType == typeof(Flow.TownState)
				|| stateType == typeof(Flow.WorldState)
				|| stateType == typeof(Flow.DungeonState);
		}

		public void UnloadAll()
		{
			// null 검사를 하지 않는다 — 씬 언로드로 이미 파괴된 인스턴스도 핸들은 살아 있어
			// 해제해야 한다. ReleaseInstance 가 파괴 여부를 구분해 처리한다.
			for (int i = 0; i < _ordered.Count; i++)
			{
				if (_ordered[i].sceneOwned == true)
				{
					continue;
				}

				AddressableHelper.ReleaseInstance(_ordered[i].instance);
			}

			_ordered.Clear();
			_byMapId.Clear();
			_lastHit = null;
			_world = null;
		}

		// 하위 호환 — 던전 종료 시 호출된다.
		public void UnloadMap()
		{
			UnloadAll();
		}

		// ── 좌표 질의 ─────────────────────────────────────────────────

		// 좌표를 담당하는 그리드를 찾는다. 못 찾으면 마지막 적중분(또는 첫 번째)으로 폴백.
		private TilemapGrid resolve(Vector2 worldPos)
		{
			if (_lastHit != null && _lastHit.grid != null && _lastHit.grid.ContainsWorldPos(worldPos) == true)
			{
				return _lastHit.grid;
			}

			for (int i = 0; i < _ordered.Count; i++)
			{
				Entry entry = _ordered[i];
				if (entry.grid != null && entry.grid.ContainsWorldPos(worldPos) == true)
				{
					_lastHit = entry;
					return entry.grid;
				}
			}

			return Current;
		}

		// 주어진 월드 위치를 플로우필드 타겟으로 재베이크.
		// 히어로가 있는 그리드에만 베이크한다 — 액트 전체를 한 필드로 다루면 빈 공간까지 계산하게 된다.
		public void BakeFlowField(Vector2 targetWorldPos)
		{
			TilemapGrid grid = resolve(targetWorldPos);
			if (grid != null)
			{
				grid.BakeFlowField(targetWorldPos);
			}
		}

		public Vector2 GetFlowDirection(Vector2 worldPos)
		{
			TilemapGrid grid = resolve(worldPos);
			return grid != null ? grid.GetFlowDirection(worldPos) : Vector2.zero;
		}

		// 반지름 radius 인 원을 장애물 밖으로 밀어낸 위치를 반환 (맵 없으면 그대로)
		public Vector2 ResolveWallCollision(Vector2 pos, float radius)
		{
			TilemapGrid grid = resolve(pos);
			return grid != null ? grid.ResolveWallCollision(pos, radius) : pos;
		}

		public Vector3Int WorldToCell(Vector2 worldPos)
		{
			TilemapGrid grid = resolve(worldPos);
			return grid != null ? grid.WorldToCell(worldPos) : default;
		}

		// 해당 월드 위치가 발사체 차단 셀인지 (맵 없으면 차단 없음)
		public bool IsProjectileBlocked(Vector2 worldPos)
		{
			TilemapGrid grid = resolve(worldPos);
			return grid != null ? grid.IsProjectileBlocked(worldPos) : false;
		}

		// 두 월드점 사이 발사체 경로 시야 확보 여부 (맵 없으면 항상 확보)
		//
		// 서로 다른 그리드에 걸친 사격은 10000 간격 때문에 사실상 발생하지 않는다.
		// 출발점 기준 그리드로 판정한다.
		public bool HasLineOfSight(Vector2 from, Vector2 to)
		{
			TilemapGrid grid = resolve(from);
			return grid != null ? grid.HasLineOfSight(from, to) : true;
		}

		// ── 스폰 마커 조회 (몬스터 설계 7장) ──────────────────────────
		//
		// 구 GetSpawnPoint(mapId, name) 은 이름 문자열 재귀 탐색이었다. 설계가 그 방식을
		// 폐기했고(복제 시 유니티가 "(1)" 을 붙여 조용히 깨진다) 호출자도 없어 삭제했다.

		private static readonly List<MonsterSpawnPoint> _emptySpawnPoints = new List<MonsterSpawnPoint>();
		private static readonly List<DungeonSpawnSlot> _emptySlots = new List<DungeonSpawnSlot>();
		private static readonly List<DungeonWaypoint> _emptyWaypoints = new List<DungeonWaypoint>();
		private static readonly List<NpcSpawnPoint> _emptyNpcPoints = new List<NpcSpawnPoint>();

		// 필드 스폰 포인트. 맵이 없으면 빈 목록(널 아님).
		public IReadOnlyList<MonsterSpawnPoint> GetSpawnPoints(int mapId)
		{
			if (_world != null)
			{
				ZoneArea zone = _world.GetZone(mapId);
				return zone != null ? zone.SpawnPoints : _emptySpawnPoints;
			}

			Entry entry;
			if (_byMapId.TryGetValue(mapId, out entry) == false || entry.grid == null)
			{
				return _emptySpawnPoints;
			}

			return entry.grid.SpawnPoints;
		}

		// NPC 배치 마커. 맵이 없으면 빈 목록(널 아님).
		public IReadOnlyList<NpcSpawnPoint> GetNpcSpawnPoints(int mapId)
		{
			if (_world != null)
			{
				ZoneArea zone = _world.GetZone(mapId);
				return zone != null ? zone.NpcPoints : _emptyNpcPoints;
			}

			Entry entry;
			if (_byMapId.TryGetValue(mapId, out entry) == false || entry.grid == null)
			{
				return _emptyNpcPoints;
			}

			return entry.grid.NpcPoints;
		}

		// 현재 로드된 맵 ID 목록 — NPC 배치가 맵 단위로 순회한다. 월드면 구역 ID 목록이다.
		public void CollectLoadedMapIds(List<int> buffer)
		{
			buffer.Clear();
			if (_world != null)
			{
				IReadOnlyList<ZoneArea> zones = _world.Zones;
				for (int i = 0; i < zones.Count; i++)
				{
					buffer.Add(zones[i].ZoneId);
				}

				return;
			}

			for (int i = 0; i < _ordered.Count; i++)
			{
				if (_ordered[i].mapId > 0)
				{
					buffer.Add(_ordered[i].mapId);
				}
			}
		}

		// 좌표가 속한 맵의 ID. 월드면 구역 ID(= Map.ID)다. 어디에도 안 들어가면 0.
		//
		// 퀘스트의 지역 한정 처치와 히어로의 현재 구역 판정이 쓴다.
		public int GetMapIdAt(Vector2 worldPos)
		{
			if (_world != null)
			{
				return _world.FindZoneIdAt(worldPos);
			}

			if (_lastHit != null && _lastHit.grid != null && _lastHit.grid.ContainsWorldPos(worldPos) == true)
			{
				return _lastHit.mapId;
			}

			for (int i = 0; i < _ordered.Count; i++)
			{
				Entry entry = _ordered[i];
				if (entry.grid != null && entry.grid.ContainsWorldPos(worldPos) == true)
				{
					return entry.mapId;
				}
			}

			return 0;
		}

		// 던전 슬롯 — SlotIndex 오름차순.
		public IReadOnlyList<DungeonSpawnSlot> GetSlots(int mapId)
		{
			Entry entry;
			if (_byMapId.TryGetValue(mapId, out entry) == false || entry.grid == null)
			{
				return _emptySlots;
			}

			return entry.grid.Slots;
		}

		// 현재 로드된 첫 맵의 던전 슬롯 — 던전은 맵이 하나뿐이라 ID 없이 접근할 수 있다.
		public IReadOnlyList<DungeonSpawnSlot> GetSlotsOfCurrentMap()
		{
			if (_ordered.Count == 0 || _ordered[0].grid == null)
			{
				return _emptySlots;
			}

			return _ordered[0].grid.Slots;
		}

		// 현재 로드된 첫 맵의 웨이포인트 — 균열 던전 몬스터 이동 경로. Index 오름차순.
		public IReadOnlyList<DungeonWaypoint> GetWaypointsOfCurrentMap()
		{
			if (_ordered.Count == 0 || _ordered[0].grid == null)
			{
				return _emptyWaypoints;
			}

			return _ordered[0].grid.Waypoints;
		}

		// 히어로 시작/부활 지점. 마커가 없으면 그리드 중심으로 폴백한다.
		// 맵 프리팹 루트 기준 경로로 오브젝트를 찾는다 — 전용 마커 컴포넌트가 없는 배치점(마을 더미 등)용.
		public Transform FindInMap(int mapId, string path)
		{
			Entry entry;
			if (_byMapId.TryGetValue(mapId, out entry) == false || entry.instance == null)
			{
				return null;
			}

			return entry.instance.transform.Find(path);
		}

		public Vector3 GetAnchorPosition(int mapId)
		{
			if (_world != null)
			{
				ZoneArea zone = _world.GetZone(mapId);
				if (zone == null)
				{
					Debug.LogError($"[MapManager] 월드 {_world.WorldId} 에 구역 {mapId} 가 없습니다.");
					return Vector3.zero;
				}

				return zone.AnchorPosition;
			}

			Entry entry;
			if (_byMapId.TryGetValue(mapId, out entry) == false || entry.grid == null)
			{
				return Vector3.zero;
			}

			MapAnchor anchor = entry.grid.Anchor;
			if (anchor != null)
			{
				return anchor.Position;
			}

			Vector2 center = entry.grid.WorldCenter;
			return new Vector3(center.x, center.y, 0f);
		}

		// 다른 월드에서 관문으로 넘어온 히어로의 도착 위치 — 출발 구역으로 돌아가는 관문의 ArrivalPoint.
		// 역방향 관문이 없으면 그 구역의 시작 지점으로 폴백한다.
		public Vector3 GetArrivalPosition(int zoneId, int fromZoneId)
		{
			TilemapGrid grid = Current;
			if (fromZoneId > 0 && grid != null)
			{
				IReadOnlyList<ZoneGate> gates = grid.Gates;
				for (int i = 0; i < gates.Count; i++)
				{
					if (gates[i] != null && gates[i].TargetZoneId == fromZoneId)
					{
						return gates[i].ArrivalPosition;
					}
				}
			}

			return GetAnchorPosition(zoneId);
		}

		// 스폰 위치를 통행 가능한 자리로 보정한다 — 벽 속 스폰을 막는다.
		public Vector3 ResolveSpawnPosition(Vector3 desired, float unitRadius)
		{
			TilemapGrid grid = resolve(desired);
			if (grid == null)
			{
				return desired;
			}

			Vector2 fixedPos = grid.ResolveWallCollision(new Vector2(desired.x, desired.y), unitRadius);
			return new Vector3(fixedPos.x, fixedPos.y, desired.z);
		}
	}
}
