using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.Quests;
using ProjectOne.Resources;
using ProjectOne.UserData;

namespace ProjectOne.Npcs
{
	// NPC 배치 (퀘스트 설계 5.3).
	//
	// 로드된 맵을 훑어 `(MapID, SpawnPointID)` 로 씬 마커를 찾고 그 자리에 프리팹을 띄운다.
	// 등장 조건은 메인 퀘스트 진행도가 정하므로, 퀘스트 상태가 바뀌면 다시 평가한다.
	//
	// 씬 마커가 없으면 테이블은 멀쩡한데 NPC 만 조용히 안 나온다 — 이 구조의 유일한 위험이라
	// 반드시 경고를 남긴다.
	public sealed class NpcSpawner : MonoBehaviour
	{
		// NpcSpawn.ID → 살아 있는 인스턴스. 조건이 풀리면 이 목록에서 지우고 파괴한다.
		private readonly Dictionary<int, GameObject> _spawned = new Dictionary<int, GameObject>();

		private readonly List<int> _mapIds = new List<int>(8);

		// 회수 대상 임시 버퍼 — 순회 중 딕셔너리를 수정하지 않기 위한 것이다.
		private readonly List<int> _removeBuffer = new List<int>(8);

		private void Awake()
		{
			EventManager.Instance.Subscribe<QuestChangeEvent>(onQuestChanged);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<QuestChangeEvent>(onQuestChanged);
		}

		// 맵이 뜬 뒤에 호출한다 — 마커를 찾을 수 있어야 한다.
		public void Refresh(CancellationToken ct = default(CancellationToken))
		{
			if (MapManager.HasInstance == false)
			{
				return;
			}

			MapManager.Instance.CollectLoadedMapIds(_mapIds);

			int cleared = Account.Instance.Quests.ClearedMainQuestId;

			despawnInactive(cleared);

			for (int i = 0; i < _mapIds.Count; i++)
			{
				refreshMap(_mapIds[i], cleared, ct);
			}
		}

		public void Clear()
		{
			Dictionary<int, GameObject>.Enumerator e = _spawned.GetEnumerator();
			while (e.MoveNext() == true)
			{
				destroyInstance(e.Current.Value);
			}

			_spawned.Clear();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void refreshMap(int mapId, int clearedMainQuestId, CancellationToken ct)
		{
			IReadOnlyList<Table_NpcSpawn.Row> spawns = QuestCatalog.GetSpawnsOfMap(mapId);
			if (spawns.Count == 0)
			{
				return;
			}

			IReadOnlyList<NpcSpawnPoint> points = MapManager.Instance.GetNpcSpawnPoints(mapId);

			for (int i = 0; i < spawns.Count; i++)
			{
				Table_NpcSpawn.Row row = spawns[i];
				if (QuestCatalog.IsSpawnActive(row, clearedMainQuestId) == false)
				{
					continue;
				}

				if (_spawned.ContainsKey(row.ID) == true)
				{
					continue;
				}

				NpcSpawnPoint point = findPoint(points, row.SpawnPointID);
				if (point == null)
				{
					Debug.LogWarning($"[NpcSpawner] 맵 {mapId} 에 SpawnPointID {row.SpawnPointID} 마커가 없습니다 — NpcSpawn {row.ID} 이 배치되지 않습니다.");
					continue;
				}

				spawnAsync(row, point.Position, ct).Forget();

				// 로드 완료 전에 같은 행을 또 띄우지 않도록 자리를 먼저 잡는다.
				_spawned[row.ID] = null;
			}
		}

		private async UniTaskVoid spawnAsync(Table_NpcSpawn.Row row, Vector3 position, CancellationToken ct)
		{
			Table_Npc.Row npc = Table_Npc.Get(row.NpcID);
			if (npc == null || string.IsNullOrEmpty(npc.PrefabName) == true)
			{
				Debug.LogWarning($"[NpcSpawner] Npc {row.NpcID} 의 PrefabName 이 비었습니다 — NpcSpawn {row.ID} 을 건너뜁니다.");
				_spawned.Remove(row.ID);
				return;
			}

			GameObject go = await AddressableHelper.TryInstantiateAsync(npc.PrefabName, this.transform, true, ct);
			if (go == null)
			{
				_spawned.Remove(row.ID);
				return;
			}

			// 로드 중에 조건이 바뀌었을 수 있다 — 자리 표시가 사라졌으면 즉시 회수한다.
			if (_spawned.ContainsKey(row.ID) == false)
			{
				destroyInstance(go);
				return;
			}

			go.transform.position = position;
			go.name = $"Npc_{npc.ID}_{npc.Name}";

			NpcUnit unit = go.GetComponent<NpcUnit>();
			if (unit == null)
			{
				unit = go.AddComponent<NpcUnit>();
			}

			unit.Setup(npc.ID, npc);
			_spawned[row.ID] = go;
		}

		// 메인 퀘스트가 진행되면서 소멸 조건에 걸린 NPC 를 걷어낸다.
		private void despawnInactive(int clearedMainQuestId)
		{
			_removeBuffer.Clear();

			Dictionary<int, GameObject>.Enumerator e = _spawned.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_NpcSpawn.Row row = Table_NpcSpawn.Get(e.Current.Key);
				if (QuestCatalog.IsSpawnActive(row, clearedMainQuestId) == false)
				{
					_removeBuffer.Add(e.Current.Key);
				}
			}

			for (int i = 0; i < _removeBuffer.Count; i++)
			{
				GameObject go;
				if (_spawned.TryGetValue(_removeBuffer[i], out go) == true)
				{
					destroyInstance(go);
				}

				_spawned.Remove(_removeBuffer[i]);
			}

			_removeBuffer.Clear();
		}

		private static NpcSpawnPoint findPoint(IReadOnlyList<NpcSpawnPoint> points, int spawnPointId)
		{
			for (int i = 0; i < points.Count; i++)
			{
				if (points[i] != null && points[i].SpawnPointId == spawnPointId)
				{
					return points[i];
				}
			}

			return null;
		}

		private static void destroyInstance(GameObject go)
		{
			if (go == null)
			{
				return;
			}

			if (AddressableHelper.ReleaseInstance(go) == false)
			{
				Object.Destroy(go);
			}
		}

		// 메인 퀘스트 진행에 따라 등장/소멸이 바뀐다. 매 프레임 순회하지 않는다 (설계 5.5).
		private void onQuestChanged(QuestChangeEvent e)
		{
			Refresh();
		}
	}
}
