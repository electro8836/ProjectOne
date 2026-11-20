using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Field
{
	// 필드 몬스터 스폰 + 개체 단위 리젠 (몬스터 설계 8장).
	//
	// - **활성 구역(히어로가 있는 구역 + 인접 구역)만** 다룬다. 활성에서 빠지는 구역은 슬롯과 살아있는 개체를 걷는다
	// - **리젠은 개체 단위다.** Count=3 인 지점에서 하나만 죽으면 그 하나만, 사망 시각 기준으로 그 자리에 다시 뜬다
	// - 던전에는 리젠이 없다 — 이 클래스는 월드 전용이다
	//
	// 리젠 시각은 MonsterRespawnClock 이 들고 있다. 슬롯은 구역이 비활성되면 사라지지만
	// 시각은 남아서, 다른 구역을 도는 동안에도 이 구역의 리젠이 흐른다.
	//
	// "최소 마릿수 유지" 방식은 쓰지 않는다. 지정된 자리에 뜨고 죽으면 그 자리에 돌아오는 일반적인 방식이다.
	public sealed class FieldMonsterSpawner : MonoBehaviour
	{
		// 리젠 시간은 MonsterSpawn.RespawnTime 이 정한다.
		// 다만 사망한 개체의 풀 반환(MonsterSpawnManager 의 반환 지연)보다 빠르면 반환 전 개체와 겹치므로
		// 그 아래로는 내려가지 않게 막는다. 스폰 실패 시 재시도 간격으로도 쓴다.
		private const float MinRespawnTime = 1.5f;

		// 리젠 대기 라벨의 몬스터 이름 색
		private const string LabelNameColor = "#FFA000";

		// 스폰 개체 1마리의 추적 정보. 죽으면 origin 자리에 다시 띄운다.
		private sealed class Slot
		{
			public SlotKey key;			// 리젠 시각 조회용. 맵을 다시 로드해도 같은 값이다
			public int monsterId;
			public int level;
			public int rewardGroupId;	// MonsterSpawn.RewardGroupID (지역 드랍)
			public float respawnTime;	// MonsterSpawn.RespawnTime (하한 적용 후)
			public RespawnType respawnType;	// MonsterSpawn.RespawnType — DailyReset 이면 06시 리젠 + 남은 시간 라벨
			public int spawnId;			// MonsterSpawn 행 ID — 필드보스(DailyReset) 처치 기록의 키
			public Vector3 origin;
			public BossArea bossArea;	// 필드보스(DailyReset) 구역 — 스폰 포인트에 붙은 BossArea

			public int instanceId;		// 살아있는 개체. 0이면 비어 있음
			public Monster monster;		// 살아있는 개체 — 구역이 비활성될 때 풀로 걷는다

			public RespawnTimerLabel label;	// 리젠 대기 중 스폰 위치에 띄운 남은 시간. 없으면 null
			public string labelTitle;		// 라벨 윗줄 — 색을 입힌 몬스터 이름. 라벨을 쓰는 슬롯만 만든다
		}

		private readonly List<Slot> _slots = new List<Slot>();

		// InstanceID → 슬롯. 사망 이벤트에서 O(1) 로 찾는다.
		private readonly Dictionary<int, Slot> _byInstance = new Dictionary<int, Slot>();

		// 지금 슬롯을 들고 있는 구역 (Table_Zone.ID)
		private readonly List<int> _activeZoneIds = new List<int>(4);

		private void Awake()
		{
			EventManager.Instance.Subscribe<UnitDiedEvent>(onUnitDied);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(onUnitDied);
		}

		// 활성 구역을 갈아끼운다 — 빠지는 구역은 슬롯과 개체를 걷고, 새로 드는 구역은 스폰 포인트를 수집한다.
		// 리젠 시각이 된 슬롯만 바로 스폰하고, 아직 안 된 슬롯은 Update 가 때가 되면 채운다.
		public void SetActiveZones(IReadOnlyList<int> zoneIds)
		{
			for (int i = _activeZoneIds.Count - 1; i >= 0; i--)
			{
				int zoneId = _activeZoneIds[i];
				if (containsZone(zoneIds, zoneId) == true)
				{
					continue;
				}

				removeZone(zoneId);
				_activeZoneIds.RemoveAt(i);
			}

			for (int i = 0; i < zoneIds.Count; i++)
			{
				int zoneId = zoneIds[i];
				if (_activeZoneIds.Contains(zoneId) == true)
				{
					continue;
				}

				_activeZoneIds.Add(zoneId);
				if (collectMap(zoneId) == 0)
				{
					Debug.LogWarning($"[FieldMonsterSpawner] 구역 {zoneId} 에 MonsterSpawnPoint 가 하나도 없습니다 — 구역이 비어 있습니다.");
				}
			}

			spawnReadySlots();
		}

		public void Clear()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				hideLabel(_slots[i]);
			}

			_slots.Clear();
			_byInstance.Clear();
			_activeZoneIds.Clear();
		}

		private static bool containsZone(IReadOnlyList<int> zoneIds, int zoneId)
		{
			for (int i = 0; i < zoneIds.Count; i++)
			{
				if (zoneIds[i] == zoneId)
				{
					return true;
				}
			}

			return false;
		}

		// 구역의 필드보스(DailyReset) 슬롯 조회 — 보스소환석이 쓴다.
		// 보스 슬롯이 없으면 false. 있으면 spawnId 와 오늘 처치돼 리젠 대기 중인지(killedToday)를 돌려준다.
		public bool TryGetBossSlot(int zoneId, out int spawnId, out bool killedToday)
		{
			spawnId = 0;
			killedToday = false;

			for (int i = 0; i < _slots.Count; i++)
			{
				Slot slot = _slots[i];
				if (slot.key.MapId != zoneId || slot.respawnType != RespawnType.DailyReset)
				{
					continue;
				}

				spawnId = slot.spawnId;
				killedToday = slot.instanceId == 0 && MonsterRespawnClock.Instance.IsBossKilledToday(zoneId, slot.spawnId) == true;
				return true;
			}

			return false;
		}

		// 구역 하나의 슬롯을 버리고 살아있는 개체를 풀로 걷는다.
		// 사망 이벤트를 쏘지 않으니 살아있던 슬롯에는 리젠 기록이 남지 않는다 — 다시 활성되면 즉시 부활한다.
		private void removeZone(int zoneId)
		{
			for (int i = _slots.Count - 1; i >= 0; i--)
			{
				Slot slot = _slots[i];
				if (slot.key.MapId != zoneId)
				{
					continue;
				}

				hideLabel(slot);

				if (slot.monster != null && slot.instanceId > 0)
				{
					_byInstance.Remove(slot.instanceId);
					MonsterSpawnManager.Instance.Despawn(slot.monster);
				}

				_slots.RemoveAt(i);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// Field 는 Map 과 ID 를 공유한다 (맵 설계 8장).
		private int collectMap(int mapId)
		{
			IReadOnlyList<MonsterSpawnPoint> points = MapManager.Instance.GetSpawnPoints(mapId);
			for (int i = 0; i < points.Count; i++)
			{
				addPoint(mapId, i, points[i]);
			}

			return points.Count;
		}

		// 스폰 포인트 하나가 조합(MonsterSpawn.GroupID)을 지정하고, 조합의 각 행이 Count 만큼 슬롯을 만든다.
		private void addPoint(int mapId, int pointIndex, MonsterSpawnPoint point)
		{
			if (point == null || point.SpawnGroupId <= 0)
			{
				return;
			}

			IReadOnlyList<Table_MonsterSpawn.Row> rows = MonsterCatalog.GetSpawnGroup(point.SpawnGroupId);
			if (rows.Count == 0)
			{
				Debug.LogWarning($"[FieldMonsterSpawner] 스폰 그룹 {point.SpawnGroupId} 에 해당하는 MonsterSpawn 행이 없습니다.");
				return;
			}

			for (int i = 0; i < rows.Count; i++)
			{
				Table_MonsterSpawn.Row row = rows[i];
				int count = (row.Count > 0) ? row.Count : 1;
				int level = (row.Level > 0) ? row.Level : 1;
				float respawnTime = (row.RespawnTime > MinRespawnTime) ? row.RespawnTime : MinRespawnTime;
				string labelTitle = (row.RespawnType == RespawnType.DailyReset) ? buildLabelTitle(row.MonsterID) : null;

				for (int n = 0; n < count; n++)
				{
					Slot slot = new Slot();
					slot.key = new SlotKey(mapId, pointIndex, i, n);
					slot.monsterId = row.MonsterID;
					slot.level = level;
					slot.rewardGroupId = row.RewardGroupID;
					slot.respawnTime = respawnTime;
					slot.respawnType = row.RespawnType;
					slot.spawnId = row.ID;
					slot.labelTitle = labelTitle;
					slot.origin = resolveOrigin(point);
					if (row.RespawnType == RespawnType.DailyReset)
					{
						// 필드보스는 반경 무작위 없이 항상 스폰 포인트 그 자리에 뜬다(최초 스폰·리젠·구역 이탈 재스폰 모두).
						slot.origin = point.Position;
						slot.bossArea = point.GetComponent<BossArea>();
						if (slot.bossArea == null)
						{
							Debug.LogError($"[FieldMonsterSpawner] 필드보스 스폰 포인트(그룹 {point.SpawnGroupId})에 BossArea 가 없습니다 — 맵 프리팹에 구역을 붙여야 합니다.", point);
						}
					}

					_slots.Add(slot);
				}
			}
		}

		// 리젠 대기 라벨의 윗줄 — 스폰 수집 시 1회만 만든다.
		private static string buildLabelTitle(int monsterId)
		{
			Table_Monster.Row monster = Table_Monster.Get(monsterId);
			if (monster == null)
			{
				return null;
			}

			return $"<color={LabelNameColor}>{monster.Name}</color>";
		}

		// 반경 안의 임의 위치를 잡되 벽 속에 박히지 않도록 보정한다.
		private Vector3 resolveOrigin(MonsterSpawnPoint point)
		{
			Vector3 desired = point.RandomPosition();
			if (MapManager.HasInstance == false)
			{
				return desired;
			}

			return MapManager.Instance.ResolveSpawnPosition(desired, 0.5f);
		}

		// 비어 있고 리젠 시각이 지난 슬롯을 채운다. 입장 시점과 매 프레임 같은 판정을 쓴다.
		private void spawnReadySlots()
		{
			MonsterRespawnClock clock = MonsterRespawnClock.Instance;
			for (int i = 0; i < _slots.Count; i++)
			{
				Slot slot = _slots[i];
				if (slot.instanceId != 0)
				{
					continue;
				}

				// 필드보스는 서버 처치 기록(06시 경계)으로 판정한다.
				if (slot.respawnType == RespawnType.DailyReset)
				{
					if (clock.IsBossKilledToday(slot.key.MapId, slot.spawnId) == true)
					{
						// 일일 리젠 슬롯만 남은 시간을 띄운다 — 필드 재입장 시에도 이 경로로 다시 뜬다.
						if (slot.label == null && RespawnTimerLabelManager.HasInstance == true)
						{
							slot.label = RespawnTimerLabelManager.Instance.Show(slot.origin, (float)DailyReset.GetRemaining().TotalSeconds, slot.labelTitle);
						}

						continue;
					}
				}
				else if (clock.IsReady(slot.key) == false)
				{
					continue;
				}

				spawn(slot);
			}
		}

		private void spawn(Slot slot)
		{
			hideLabel(slot);
			MonsterRespawnClock.Instance.Clear(slot.key);

			// 스폰 중 표시 — 완료 전에 Update 가 같은 슬롯을 또 스폰하는 것을 막는다.
			slot.instanceId = PendingInstanceId;
			spawnAsync(slot).Forget();
		}

		// 아직 InstanceID 를 모르는 상태. UnitFactory 가 0 을 발급하지 않으므로 충돌하지 않는다.
		private const int PendingInstanceId = -1;

		private async UniTaskVoid spawnAsync(Slot slot)
		{
			Monster monster = await MonsterSpawnManager.Instance.SpawnOneShotAsync(slot.monsterId, slot.level, slot.origin, slot.rewardGroupId);
			if (monster == null)
			{
				// 매 프레임 재시도하지 않도록 간격을 둔다.
				slot.instanceId = 0;
				MonsterRespawnClock.Instance.SetRespawn(slot.key, MinRespawnTime);
				return;
			}

			// 스폰을 기다리는 동안 구역이 비활성됐다 — 주인 없는 개체가 남지 않게 바로 걷는다.
			if (_slots.Contains(slot) == false)
			{
				MonsterSpawnManager.Instance.Despawn(monster);
				return;
			}

			slot.instanceId = monster.GetID();
			slot.monster = monster;
			_byInstance[slot.instanceId] = slot;

			// 필드보스 표시 — 처치 보상이 배치 정산 대신 FieldBossKill 로 간다. Field.ID 와 Map.ID 는 같다.
			if (slot.respawnType == RespawnType.DailyReset)
			{
				monster.SetFieldBoss(slot.key.MapId, slot.spawnId, slot.bossArea);
			}
		}

		private void onUnitDied(UnitDiedEvent e)
		{
			Slot slot;
			if (e.UnitType != UnitType.Monster || _byInstance.TryGetValue(e.InstanceID, out slot) == false)
			{
				return;
			}

			_byInstance.Remove(e.InstanceID);
			slot.instanceId = 0;
			slot.monster = null;

			// 일일 리젠은 다음 초기화 시각(06시)에 돌아온다. 서버 기록은 FieldBossReward 가 응답으로 덮는다.
			if (slot.respawnType == RespawnType.DailyReset)
			{
				MonsterRespawnClock.Instance.SetBossKilled(slot.key.MapId, slot.spawnId, DailyReset.GetResetDay());
				return;
			}

			// 사망 시각 기준으로 그 자리에 다시 뜬다 (설계 8장).
			// 시각은 원장에 남으므로 다른 필드를 도는 동안에도 흐른다.
			MonsterRespawnClock.Instance.SetRespawn(slot.key, slot.respawnTime);
		}

		// 종료 중에는 매니저가 먼저 파괴될 수 있다 — HasInstance 로 거른다.
		private void hideLabel(Slot slot)
		{
			if (slot.label == null)
			{
				return;
			}

			if (RespawnTimerLabelManager.HasInstance == true)
			{
				RespawnTimerLabelManager.Instance.Hide(slot.label);
			}

			slot.label = null;
		}

		private void Update()
		{
			spawnReadySlots();
			respawnAbandonedBosses();
		}

		// 히어로가 보스 구역을 벗어났는데 보스에 싸운 흔적이 있으면 회수하고 그 자리에 새로 스폰한다.
		// 고칠 곳을 하나씩 되돌리는 대신 스폰 경로를 그대로 타서 갓 스폰한 상태로 만든다.
		// 회수는 처치가 아니다 — 처치 기록·보상·리젠 대기가 생기지 않는다(removeZone 과 같은 경로).
		private void respawnAbandonedBosses()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				Slot slot = _slots[i];
				Monster monster = slot.monster;
				if (slot.bossArea == null || monster == null || slot.instanceId <= 0 || monster.IsDead == true)
				{
					continue;
				}

				if (monster.IsHeroInBossArea() == true || monster.HasFought() == false)
				{
					continue;
				}

				_byInstance.Remove(slot.instanceId);
				slot.instanceId = 0;
				slot.monster = null;
				MonsterSpawnManager.Instance.Despawn(monster);
				spawn(slot);
			}
		}
	}
}
