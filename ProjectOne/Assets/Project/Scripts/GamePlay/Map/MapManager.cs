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
	// 필드(4.Field)는 **액트 하나의 필드 그리드맵을 전부 동시에 로드**하므로 여러 개를 함께 보유한다.
	// 던전(5.Dungeon)은 단계마다 하나만 쓴다 — 같은 구조로 1개만 로드하면 된다.
	//
	// 배치 좌표는 테이블이 아니라 코드가 정한다:
	//   ( (필드Order - 1) × Spacing,  (액트Order - 1) × Spacing,  0 )
	//
	// 플로우필드 계산(좌표/통행/BFS)만 담당 — "누구를 향해 베이크할지"는 호출자(전투 규칙)가 정한다.
	public class MapManager : MonoSingleton<MapManager>
	{
		// 그리드맵끼리 절대 겹치지 않도록 띄우는 간격
		public const float MapSpacing = 10000f;

		// 상시 매니저 — 마을·필드·던전이 모두 이 매니저를 거쳐 맵을 띄운다.
		// 맵 로드와 청소의 유일한 주체이므로 씬과 함께 죽으면 안 된다.
		protected override bool Persistent => true;

		private sealed class Entry
		{
			public int mapId;
			public GameObject instance;
			public TilemapGrid grid;
			public Vector3 origin;
		}

		// Map.ID → 로드된 그리드맵
		private readonly Dictionary<int, Entry> _byMapId = new Dictionary<int, Entry>();

		// 좌표 질의가 매번 딕셔너리를 순회하지 않도록 리스트로도 들고 있는다.
		private readonly List<Entry> _ordered = new List<Entry>(8);

		// 마지막으로 질의된 그리드 — 히어로는 대개 한 그리드 안에 머무르므로 캐시가 거의 항상 적중한다.
		private Entry _lastHit;

		// 씬에 배치된 컨테이너. 없으면 ensureContainerRoot 가 **활성 씬에** 만든다(매니저 자식이 아니다).
		private MapContainer _container;

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

		// 액트 단위 로드 — 그 액트에 속한 Field 전부를 좌표 규칙대로 배치한다.
		public async UniTask<bool> LoadActAsync(int actId, CancellationToken ct = default)
		{
			UnloadAll();

			Table_Act.Row act = Table_Act.Get(actId);
			if (act == null)
			{
				Debug.LogError($"[MapManager] Table_Act.Get({actId}) == null");
				return false;
			}

			List<Table_Field.Row> fields = new List<Table_Field.Row>();
			Dictionary<int, Table_Field.Row> all = Table_Field.All();
			Dictionary<int, Table_Field.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (e.Current.Value.ActID == actId)
				{
					fields.Add(e.Current.Value);
				}
			}

			if (fields.Count == 0)
			{
				Debug.LogError($"[MapManager] 액트 {actId} 에 속한 Field 가 없습니다.");
				return false;
			}

			bool anyLoaded = false;
			for (int i = 0; i < fields.Count; i++)
			{
				Table_Field.Row field = fields[i];
				Vector3 origin = GetFieldOrigin(act.Order, field.Order);

				// Field 는 Map 과 ID 를 공유한다 (맵 설계 8장).
				bool ok = await loadOneAsync(field.ID, origin, ct);
				anyLoaded |= ok;
			}

			return anyLoaded;
		}

		// 배치 좌표 규칙 — 액트는 Y축, 필드는 X축으로 나열한다.
		public static Vector3 GetFieldOrigin(int actOrder, int fieldOrder)
		{
			float x = (fieldOrder - 1) * MapSpacing;
			float y = (actOrder - 1) * MapSpacing;
			return new Vector3(x, y, 0f);
		}

		// Map.ID 로 그 맵이 배치된 원점을 돌려준다. 로드돼 있지 않으면 테이블에서 계산한다.
		public Vector3 GetOriginOfMap(int mapId)
		{
			Entry entry;
			if (_byMapId.TryGetValue(mapId, out entry) == true)
			{
				return entry.origin;
			}

			Table_Field.Row field = Table_Field.Get(mapId);
			if (field == null)
			{
				return Vector3.zero;
			}

			Table_Act.Row act = Table_Act.Get(field.ActID);
			return GetFieldOrigin(act != null ? act.Order : 1, field.Order);
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
				|| stateType == typeof(Flow.FieldState)
				|| stateType == typeof(Flow.DungeonState);
		}

		public void UnloadAll()
		{
			// null 검사를 하지 않는다 — 씬 언로드로 이미 파괴된 인스턴스도 핸들은 살아 있어
			// 해제해야 한다. ReleaseInstance 가 파괴 여부를 구분해 처리한다.
			for (int i = 0; i < _ordered.Count; i++)
			{
				AddressableHelper.ReleaseInstance(_ordered[i].instance);
			}

			_ordered.Clear();
			_byMapId.Clear();
			_lastHit = null;
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
		private static readonly List<NpcSpawnPoint> _emptyNpcPoints = new List<NpcSpawnPoint>();

		// 필드 스폰 포인트. 맵이 없으면 빈 목록(널 아님).
		public IReadOnlyList<MonsterSpawnPoint> GetSpawnPoints(int mapId)
		{
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
			Entry entry;
			if (_byMapId.TryGetValue(mapId, out entry) == false || entry.grid == null)
			{
				return _emptyNpcPoints;
			}

			return entry.grid.NpcPoints;
		}

		// 현재 로드된 맵 ID 목록 — NPC 배치가 맵 단위로 순회한다.
		public void CollectLoadedMapIds(List<int> buffer)
		{
			buffer.Clear();
			for (int i = 0; i < _ordered.Count; i++)
			{
				if (_ordered[i].mapId > 0)
				{
					buffer.Add(_ordered[i].mapId);
				}
			}
		}

		// 좌표가 속한 맵의 ID. 어느 그리드에도 안 들어가면 0.
		//
		// 퀘스트의 지역 한정 처치(KillMonster / QuestParam_3)가 쓴다 — 그리드가 10000 간격으로
		// 떨어져 있어 좌표만으로 어느 지역인지 확정된다.
		public int GetMapIdAt(Vector2 worldPos)
		{
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

		// 히어로 시작/부활 지점. 마커가 없으면 그리드 중심으로 폴백한다.
		public Vector3 GetAnchorPosition(int mapId)
		{
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
