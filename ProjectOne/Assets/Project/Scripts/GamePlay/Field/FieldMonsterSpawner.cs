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
	// - 액트에 속한 모든 맵의 `MonsterSpawnPoint` 를 입장 시 한 번에 스폰한다
	// - **리젠은 개체 단위다.** Count=3 인 지점에서 하나만 죽으면 그 하나만, 사망 시각 기준으로 그 자리에 다시 뜬다
	// - 던전에는 리젠이 없다 — 이 클래스는 필드 전용이다
	//
	// "최소 마릿수 유지" 방식은 쓰지 않는다. 지정된 자리에 뜨고 죽으면 그 자리에 돌아오는 일반적인 방식이다.
	public sealed class FieldMonsterSpawner : MonoBehaviour
	{
		// 리젠 딜레이는 전 지역 동일하므로 코드 상수다 (설계 8장).
		// 설계가 수치를 주지 않아 임의로 정했다 — 밸런싱 시 조정한다.
		private const float RespawnDelay = 8f;

		// 스폰 개체 1마리의 추적 정보. 죽으면 origin 자리에 다시 띄운다.
		private sealed class Slot
		{
			public int monsterId;
			public int level;
			public Vector3 origin;

			public int instanceId;		// 살아있는 개체. 0이면 비어 있음
			public float respawnAt;		// 리젠 예정 시각. 0이면 예약 없음
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

		// 액트의 모든 맵을 훑어 스폰 포인트를 수집하고 즉시 스폰한다.
		public void BeginAct(int actId)
		{
			Clear();

			int pointCount = 0;
			Dictionary<int, Table_MapStage.Row> all = Table_MapStage.All();
			Dictionary<int, Table_MapStage.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MapStage.Row stage = e.Current.Value;
				if (stage.ActID != actId)
				{
					continue;
				}

				pointCount += collectMap(stage.ID);
			}

			if (pointCount == 0)
			{
				Debug.LogWarning($"[FieldMonsterSpawner] 액트 {actId} 의 맵에 MonsterSpawnPoint 가 하나도 없습니다 — 필드가 비어 있습니다.");
			}

			spawnAllEmpty();
		}

		public void Clear()
		{
			_slots.Clear();
			_byInstance.Clear();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// MapStage 는 Map 과 ID 를 공유한다 (맵 설계 8장).
		private int collectMap(int mapId)
		{
			IReadOnlyList<MonsterSpawnPoint> points = MapManager.Instance.GetSpawnPoints(mapId);
			for (int i = 0; i < points.Count; i++)
			{
				addPoint(points[i]);
			}

			return points.Count;
		}

		// 스폰 포인트 하나가 조합(MonsterSpawn.GroupID)을 지정하고, 조합의 각 행이 Count 만큼 슬롯을 만든다.
		private void addPoint(MonsterSpawnPoint point)
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

				for (int n = 0; n < count; n++)
				{
					Slot slot = new Slot();
					slot.monsterId = row.MonsterID;
					slot.level = level;
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

		private void spawnAllEmpty()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				if (_slots[i].instanceId == 0)
				{
					spawn(_slots[i]);
				}
			}
		}

		private void spawn(Slot slot)
		{
			slot.respawnAt = 0f;

			// 스폰 중 표시 — 완료 전에 Update 가 같은 슬롯을 또 스폰하는 것을 막는다.
			slot.instanceId = PendingInstanceId;
			spawnAsync(slot).Forget();
		}

		// 아직 InstanceID 를 모르는 상태. UnitFactory 가 0 을 발급하지 않으므로 충돌하지 않는다.
		private const int PendingInstanceId = -1;

		private async UniTaskVoid spawnAsync(Slot slot)
		{
			Monster monster = await MonsterSpawnManager.Instance.SpawnOneShotAsync(slot.monsterId, slot.level, slot.origin);
			if (monster == null)
			{
				slot.instanceId = 0;
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
			slot.respawnAt = Time.time + RespawnDelay;
		}

		private void Update()
		{
			float now = Time.time;
			for (int i = 0; i < _slots.Count; i++)
			{
				Slot slot = _slots[i];
				if (slot.instanceId != 0 || slot.respawnAt <= 0f || now < slot.respawnAt)
				{
					continue;
				}

				spawn(slot);
			}
		}
	}
}
