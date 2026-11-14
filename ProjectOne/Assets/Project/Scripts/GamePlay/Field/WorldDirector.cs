using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Flow;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Npcs;
using ProjectOne.UI;
using ProjectOne.Unit;

namespace ProjectOne.Field
{
	// 월드 씬의 오케스트레이터. 씬에는 맵(WorldMap)만 배치돼 있고 나머지는 코드가 직접 생성한다.
	//
	// 월드 안의 구역은 끊김 없이 이어져 있다 — 구역 이동에 로드가 없다. 히어로 좌표로 현재 구역을 판정하고,
	// 몬스터는 현재 구역과 그 인접 구역에서만 활성화한다(FieldMonsterSpawner).
	// 다른 월드로 갈 때는 WorldState 로 다시 들어가 씬을 바꾼다.
	//
	// 구역 이동에서는 회복하지 않는다 — 월드 진행이 연속된 소모전이 되도록 (기반테이블 5.3).
	public sealed class WorldDirector : MonoBehaviour
	{
		private const string ContinuePopupAddress = "UIPrefab_ContinuePopup";

		private static WorldDirector _instance;

		// 사망 팝업이 떠 있는 동안 들어오는 사망 알림을 무시한다.
		// 부활 직후 남은 몬스터가 다시 죽이는 경우 팝업이 겹쳐 열릴 수 있다.
		private bool _handlingDeath;

		private System.Action<UnitDiedEvent> _onUnitDied;

		// 현재 월드 (Table_World.ID)
		private int _currentWorldId;

		// 현재 히어로가 있는 구역 (Table_Zone.ID = Map.ID)
		private int _currentZoneId;

		private Hero _hero;

		// 씬에 배치된 월드 맵. Begin 에서 찾는다.
		private WorldMap _world;

		// 관문 이동(월드 전환 로딩) 중 재진입 차단
		private bool _movingByGate;

		// 스폰 포인트 수집 + 개체 단위 리젠. 월드 전용이다(던전에는 리젠이 없다).
		private FieldMonsterSpawner _spawner;

		// NPC 배치. 구역에도 NpcSpawn 행이 걸릴 수 있다 (MapID 는 MapType 을 가리지 않는다).
		private NpcSpawner _npcSpawner;

		// 활성 구역 목록 — 현재 구역 + 인접 구역. 구역이 바뀔 때마다 다시 채운다.
		private readonly List<int> _activeZones = new List<int>(4);

		// 플로우필드 재베이크·구역 판정 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		public int CurrentWorldId => _currentWorldId;
		public int CurrentZoneId => _currentZoneId;

		public static WorldDirector EnsureInstance()
		{
			if (_instance != null)
			{
				return _instance;
			}

			GameObject go = new GameObject("WorldDirector");
			_instance = go.AddComponent<WorldDirector>();
			_instance._spawner = go.AddComponent<FieldMonsterSpawner>();
			_instance._npcSpawner = go.AddComponent<NpcSpawner>();

			// 처치 경험치 지급기는 이벤트 구독형이라 킬이 나기 전에 살아 있어야 한다.
			// MonoSingleton 이 접근 시점에 자동 생성하므로 여기서 한 번 건드린다.
			MonsterKillReward.Instance.Touch();
			FieldBossReward.Instance.Touch();
			return _instance;
		}

		public static bool HasInstance => _instance != null;

		public static WorldDirector Instance => _instance;

		private void Awake()
		{
			// 사망은 엣지 이벤트다 — Update 로 IsDead 를 훑으면 같은 프레임에 여러 번 잡힌다.
			_onUnitDied = onUnitDied;
			EventManager.Instance.Subscribe<UnitDiedEvent>(_onUnitDied);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(_onUnitDied);

			// 팝업 도중 씬이 바뀌면 정지된 채로 남는다.
			Time.timeScale = 1f;

			// 풀 오브젝트는 씬과 함께 사라지지만 Addressable 핸들은 명시적으로 놓아야 한다.
			if (DropManager.HasInstance == true)
			{
				DropManager.Instance.Clear();
			}

			if (UIManager.HasInstance == true)
			{
				UIManager.Instance.ReleaseWorldGauge();
			}

			if (_instance == this)
			{
				_instance = null;
			}
		}

		// 월드 진입 — 씬은 WorldState 가 이미 로드했다. 씬의 맵을 등록하고 히어로를 그 구역에 놓는다.
		// fromZoneId 가 0 이 아니면 그 구역으로 돌아가는 관문 앞에 도착한다.
		public async UniTask Begin(int zoneId, int fromZoneId, CancellationToken ct)
		{
			Table_Zone.Row zone = Table_Zone.Get(zoneId);
			if (zone == null)
			{
				Debug.LogError($"[WorldDirector] Table_Zone.Get({zoneId}) == null");
				return;
			}

			// 이전 씬(마을·던전)의 유닛을 먼저 걷어낸다 — 아래에서 히어로를 새로 스폰한다.
			GameplaySceneSetup.ClearGameplayUnits();

			// 카메라 리그는 씬과 함께 파괴된다. 없으면 vcam 이 없어 화면이 히어로를 따라가지 않는다.
			await GameplaySceneSetup.EnsureCameraAsync(ct);

			_world = Object.FindAnyObjectByType<WorldMap>();
			if (MapManager.Instance.RegisterWorldMap(_world) == false)
			{
				return;
			}

			if (_world.WorldId != zone.WorldID)
			{
				Debug.LogError($"[WorldDirector] 씬의 WorldMap({_world.WorldId}) 과 구역 {zoneId} 의 월드({zone.WorldID}) 가 다릅니다.");
			}

			_currentWorldId = zone.WorldID;
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

			if (_npcSpawner != null)
			{
				_npcSpawner.Clear();
				_npcSpawner.Refresh(ct);
			}

			Vector3 spawnPos = MapManager.Instance.GetArrivalPosition(zoneId, fromZoneId);
			_hero = await UnitFactory.Instance.CreateHeroAsync(spawnPos, Faction.Player, true, ct);

			// 월드 오브젝트 풀은 첫 처치 전에 준비돼 있어야 한다 — 없으면 보상 드랍이 유실된다.
			await DropManager.Instance.PrepareAsync(ct);

			// 유닛 위에 뜨는 게이지(보스 캐스팅·상호작용 진행)를 Canvas_World 에 올린다.
			await UIManager.Instance.EnsureWorldGaugeAsync(ct);

			setCurrentZone(zoneId);
		}

		// 구역 이동 — 같은 월드면 그 구역 시작 지점으로 옮기고, 다른 월드면 씬을 바꾼다.
		// 월드 화면·개발용 이동이 쓴다. 회복은 없다.
		public void MoveToZone(int zoneId)
		{
			Table_Zone.Row zone = Table_Zone.Get(zoneId);
			if (zone == null)
			{
				Debug.LogError($"[WorldDirector] Table_Zone.Get({zoneId}) == null");
				return;
			}

			if (zone.WorldID != _currentWorldId)
			{
				GameFlow.Instance.ChangeStateAsync(new WorldState(zoneId)).Forget();
				return;
			}

			if (_hero == null)
			{
				return;
			}

			_hero.transform.position = MapManager.Instance.GetAnchorPosition(zoneId);
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);
			setCurrentZone(zoneId);
		}

		// 다른 월드로 넘어가는 관문(ZoneGate)으로 이동 — 도착 위치는 목적지 월드에서 fromZoneId 로 돌아가는 관문이다.
		// fromZoneId 가 0 이면(관문이 구역 밖에 놓인 경우) 현재 구역을 쓴다.
		public void MoveByGate(int targetZoneId, int fromZoneId)
		{
			if (_movingByGate == true)
			{
				return;
			}

			Table_Zone.Row zone = Table_Zone.Get(targetZoneId);
			if (zone == null)
			{
				Debug.LogError($"[WorldDirector] Table_Zone.Get({targetZoneId}) == null");
				return;
			}

			if (zone.WorldID == _currentWorldId)
			{
				MoveToZone(targetZoneId);
				return;
			}

			_movingByGate = true;
			int from = fromZoneId > 0 ? fromZoneId : _currentZoneId;
			GameFlow.Instance.ChangeStateAsync(new WorldState(targetZoneId, from)).Forget();
		}

		// 현재 구역 갱신 — 활성 구역(현재 + 인접)을 다시 정하고 스포너에 넘긴다.
		private void setCurrentZone(int zoneId)
		{
			if (zoneId == _currentZoneId)
			{
				return;
			}

			_currentZoneId = zoneId;
			ZoneProgress.SetLastVisitedZoneId(zoneId);
			EventManager.Instance.Publish(new ZoneChangedEvent(zoneId));

			collectActiveZones(zoneId);
			if (_spawner != null)
			{
				_spawner.SetActiveZones(_activeZones);
			}
		}

		private void collectActiveZones(int zoneId)
		{
			_activeZones.Clear();
			_activeZones.Add(zoneId);

			ZoneArea area = _world != null ? _world.GetZone(zoneId) : null;
			if (area == null || area.NeighborZoneIds == null)
			{
				return;
			}

			IReadOnlyList<int> neighbors = area.NeighborZoneIds;
			for (int i = 0; i < neighbors.Count; i++)
			{
				if (neighbors[i] > 0 && _activeZones.Contains(neighbors[i]) == false)
				{
					_activeZones.Add(neighbors[i]);
				}
			}
		}

		private void Update()
		{
			updateHeroCell();
		}

		// ── 사망 / 부활 ───────────────────────────────────────────────

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (evt.UnitType != UnitType.Hero || _handlingDeath == true)
			{
				return;
			}

			_handlingDeath = true;
			handleDeathAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		// 월드 사망은 되돌릴 수 없는 실패가 아니다 — 조건 없이 현재 구역 시작 지점에서 다시 시작한다.
		private async UniTaskVoid handleDeathAsync(CancellationToken ct)
		{
			// 연출은 timeScale 1 에서 돈다 — 0 이면 사망 애니메이션도 카메라 줌도 멈춘다.
			await DeathSequence.PlayAsync(ct);

			Time.timeScale = 0f;

			ContinuePopup popup = await UIManager.Instance.OpenWindowAsync<ContinuePopup>(ContinuePopupAddress, ct);
			if (popup != null)
			{
				await popup.ShowAsync(ContinuePopupData.ForFieldDeath(), ct);
				await UIManager.Instance.CloseWindowAsync(false);
			}

			Time.timeScale = 1f;

			reviveAtStart();
			DeathSequence.ResetZoom();
			_handlingDeath = false;
		}

		// 현재 구역 시작 지점에서 완전 회복 상태로 되살린다.
		// UnitSpawnedEvent 를 다시 발행해야 카메라·조이스틱이 대상을 새로 잡는다
		// (DungeonDirector.reviveHeroes 와 같은 규약).
		private void reviveAtStart()
		{
			if (_hero == null)
			{
				return;
			}

			Vector3 spawnPos = MapManager.Instance.GetAnchorPosition(_currentZoneId);

			_hero.OnSpawnReset(spawnPos);
			_hero.transform.position = spawnPos;
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

			EventManager.Instance.Publish(new UnitSpawnedEvent(_hero, UnitType.Hero, _hero.GetID(), 0));
		}

		// 기준 히어로의 셀이 바뀌었을 때만 플로우필드를 다시 굽고 현재 구역을 판정한다.
		// 구역 사이 빈틈(어느 구역에도 안 드는 좌표)에서는 이전 구역을 유지한다.
		private void updateHeroCell()
		{
			if (MapManager.HasInstance == false || MapManager.Instance.HasMap == false)
			{
				return;
			}

			if (_hero == null || _hero.IsDead == true)
			{
				return;
			}

			Vector3Int currentCell = MapManager.Instance.WorldToCell(_hero.transform.position);
			if (currentCell == _lastHeroCell)
			{
				return;
			}

			_lastHeroCell = currentCell;
			MapManager.Instance.BakeFlowField(_hero.transform.position);

			int zoneId = MapManager.Instance.GetMapIdAt(_hero.transform.position);
			if (zoneId > 0)
			{
				setCurrentZone(zoneId);
			}
		}
	}
}
