using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Map;
using ProjectOne.Npcs;
using ProjectOne.Quests;
using ProjectOne.Unit;

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

		private Hero _hero;

		public static bool HasInstance => _instance != null;

		public static TownDirector Instance => _instance;

		public int TownMapId => _townMapId;

		public Hero Hero => _hero;

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
			// 이전 씬(필드·던전)의 유닛을 먼저 걷어낸다 — 아래에서 히어로를 새로 스폰하므로
			// 이게 없으면 마을을 드나들 때마다 히어로가 쌓인다.
			GameplaySceneSetup.ClearGameplayUnits();

			// 카메라는 맵·히어로가 없어도 먼저 세운다 — 실패해도 흐름을 막지 않는다.
			await GameplaySceneSetup.EnsureCameraAsync(ct);

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

			// 맵이 떠야 NPC 마커와 히어로 시작 지점을 찾을 수 있다.
			_npcSpawner.Refresh(ct);

			await spawnHeroAsync(ct);
		}

		// 마을 히어로 — 전투가 없으므로 자동전투 두뇌를 붙이지 않는다(조작만).
		// 마을 귀환은 전체 회복 지점이다 (기반테이블 5.3).
		private async UniTask spawnHeroAsync(CancellationToken ct)
		{
			Vector3 spawnPos = MapManager.Instance.GetAnchorPosition(_townMapId);

			_hero = await UnitFactory.Instance.CreateHeroAsync(spawnPos, Faction.Player, false, ct);
			if (_hero == null)
			{
				// UnitFactory 가 이미 원인을 로그로 남긴다(주소 미입력 / 프리팹에 Hero 컴포넌트 없음 등).
				return;
			}

			if (_hero.Vitals != null)
			{
				_hero.Vitals.FullHeal();
			}
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
