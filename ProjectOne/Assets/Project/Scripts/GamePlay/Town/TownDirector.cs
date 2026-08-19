using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Map;
using ProjectOne.Npcs;
using ProjectOne.Quests;

namespace ProjectOne.Town
{
	// 마을(3.Town)의 오케스트레이터. FieldDirector 와 동형이다.
	//
	// 마을도 그리드맵 프리팹이다 — 씬은 비어 있고 코드가 띄운다 (맵 설계 8장).
	// NPC 배치가 씬 마커를 필요로 하므로 마을 맵이 MapManager 를 거쳐야 마커를 찾을 수 있다.
	public sealed class TownDirector : MonoBehaviour
	{
		private static TownDirector _instance;

		private NpcSpawner _npcSpawner;

		private int _townMapId;

		public static bool HasInstance => _instance != null;

		public static TownDirector Instance => _instance;

		public int TownMapId => _townMapId;

		public static TownDirector EnsureInstance()
		{
			if (_instance != null)
			{
				return _instance;
			}

			GameObject go = new GameObject("TownDirector");
			_instance = go.AddComponent<TownDirector>();
			_instance._npcSpawner = go.AddComponent<NpcSpawner>();

			// 퀘스트 추적기는 이벤트 구독형이라 진행이 일어나기 전에 살아 있어야 한다.
			QuestTracker.Instance.Touch();
			return _instance;
		}

		private void OnDestroy()
		{
			if (_instance == this)
			{
				_instance = null;
			}
		}

		public async UniTask Begin(CancellationToken ct)
		{
			_townMapId = findTownMapId();
			if (_townMapId <= 0)
			{
				Debug.LogWarning("[TownDirector] MapType=Town 인 Map 행이 없습니다 — 마을 맵과 NPC 배치를 건너뜁니다.");
				return;
			}

			bool loaded = await MapManager.Instance.LoadMapAsync(_townMapId, ct);
			if (loaded == false)
			{
				Debug.LogWarning($"[TownDirector] 마을 맵 {_townMapId} 로드 실패 — NPC 배치를 건너뜁니다.");
				return;
			}

			// 맵이 떠야 NPC 마커를 찾을 수 있다.
			_npcSpawner.Refresh(ct);
		}

		// 마을은 하나뿐이다. 여러 개면 첫 번째를 쓰고 경고한다.
		private static int findTownMapId()
		{
			int found = 0;

			Dictionary<int, Table_Map.Row> all = Table_Map.All();
			Dictionary<int, Table_Map.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Map.Row row = e.Current.Value;
				if (row.MapType != MapType.Town)
				{
					continue;
				}

				if (found > 0)
				{
					Debug.LogWarning($"[TownDirector] MapType=Town 인 Map 이 둘 이상입니다 ({found}, {row.ID}) — 앞의 것을 씁니다.");
					continue;
				}

				found = row.ID;
			}

			return found;
		}
	}
}
