using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Unit;

namespace ProjectOne.Field
{
	// 필드 몬스터 스폰 + 개체 단위 리젠 (몬스터 설계 8장).
	//
	// - **히어로가 있는 필드 하나만** 다룬다. 필드를 떠나면 슬롯을 버리고 새 필드 것으로 갈아끼운다
	// - **리젠은 개체 단위다.** Count=3 인 지점에서 하나만 죽으면 그 하나만, 사망 시각 기준으로 그 자리에 다시 뜬다
	// - 던전에는 리젠이 없다 — 이 클래스는 필드 전용이다
	//
	// 리젠 시각은 MonsterRespawnClock 이 들고 있다. 슬롯은 필드를 떠나면 사라지지만
	// 시각은 남아서, 다른 필드를 도는 동안에도 이 필드의 리젠이 흐른다.
	//
	// "최소 마릿수 유지" 방식은 쓰지 않는다. 지정된 자리에 뜨고 죽으면 그 자리에 돌아오는 일반적인 방식이다.
	public sealed class FieldMonsterSpawner : MonoBehaviour
	{
		// 리젠 시간은 MonsterSpawn.RespawnTime 이 정한다.
		// 다만 사망한 개체의 풀 반환(MonsterSpawnManager 의 반환 지연)보다 빠르면 반환 전 개체와 겹치므로
		// 그 아래로는 내려가지 않게 막는다. 스폰 실패 시 재시도 간격으로도 쓴다.
		private const float MinRespawnTime = 1.5f;

		// 스폰 개체 1마리의 추적 정보. 죽으면 origin 자리에 다시 띄운다.
		private sealed class Slot
		{
			public SlotKey key;			// 리젠 시각 조회용. 맵을 다시 로드해도 같은 값이다
			public int monsterId;
			public int level;
			public int rewardGroupId;	// MonsterSpawn.RewardGroupID (지역 드랍)
			public float respawnTime;	// MonsterSpawn.RespawnTime (하한 적용 후)
			public Vector3 origin;

			public int instanceId;		// 살아있는 개체. 0이면 비어 있음
		}

		private readonly List<Slot> _slots = new List<Slot>();

		// InstanceID → 슬롯. 사망 이벤트에서 O(1) 로 찾는다.
		private readonly Dictionary<int, Slot> _byInstance = new Dictionary<int, Slot>();

		private void Awake()
		{
			EventManager.Instance.Subscribe<UnitDiedEvent>(onUnitDied);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(onUnitDied);
		}

		// 필드 하나의 스폰 포인트를 수집하고, 리젠 시각이 된 슬롯만 스폰한다.
		// 아직 시각이 안 된 슬롯은 Update 가 때가 되면 채운다.
		public void BeginField(int fieldId)
		{
			Clear();

			int pointCount = collectMap(fieldId);
			if (pointCount == 0)
			{
				Debug.LogWarning($"[FieldMonsterSpawner] 필드 {fieldId} 에 MonsterSpawnPoint 가 하나도 없습니다 — 필드가 비어 있습니다.");
			}

			spawnReadySlots();
		}

		// 필드를 떠날 때. 살아있는 개체 회수는 MonsterSpawnManager.ClearAlive 가 하므로 여기서는 슬롯만 버린다.
		// ClearAlive 는 사망 이벤트를 쏘지 않으니 살아있던 슬롯에는 리젠 기록이 남지 않는다 — 재입장 시 즉시 부활한다.
		public void EndField()
		{
			Clear();
		}

		public void Clear()
		{
			_slots.Clear();
			_byInstance.Clear();
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

				for (int n = 0; n < count; n++)
				{
					Slot slot = new Slot();
					slot.key = new SlotKey(mapId, pointIndex, i, n);
					slot.monsterId = row.MonsterID;
					slot.level = level;
					slot.rewardGroupId = row.RewardGroupID;
					slot.respawnTime = respawnTime;
					slot.origin = resolveOrigin(point);
					_slots.Add(slot);
				}
			}
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
				if (slot.instanceId != 0 || clock.IsReady(slot.key) == false)
				{
					continue;
				}

				spawn(slot);
			}
		}

		private void spawn(Slot slot)
		{
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

			slot.instanceId = monster.GetID();
			_byInstance[slot.instanceId] = slot;
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

			// 사망 시각 기준으로 그 자리에 다시 뜬다 (설계 8장).
			// 시각은 원장에 남으므로 다른 필드를 도는 동안에도 흐른다.
			MonsterRespawnClock.Instance.SetRespawn(slot.key, slot.respawnTime);
		}

		private void Update()
		{
			spawnReadySlots();
		}
	}
}
