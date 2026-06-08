using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;
using ProjectOne.Unit;
using ProjectOne.Map;
using ProjectOne.Resources;

namespace ProjectOne.Test
{
	// MapTest 씬에 부착 — 인스펙터에서 영웅/몬스터 스펙을 직접 설정, Play 시 일괄 스폰
	public class MapTestSpawner : MonoBehaviour
	{
		[System.Serializable]
		public struct HeroEntry
		{
			public int characterId;          // Table_Character ID
			public Vector3 position;
			public Faction faction;          // None 이면 Player 기본
			public bool autoControl;         // true 면 자동전투 AI, false 면 플레이어 직접조작
		}

		[System.Serializable]
		public struct MonsterEntry
		{
			public int monsterId;            // Table_Monster ID
			public Vector3 position;
			public Faction faction;          // None 이면 Enemy 기본
		}

		[SerializeField] List<HeroEntry> _heroes = new List<HeroEntry>();
		[SerializeField] List<MonsterEntry> _monsters = new List<MonsterEntry>();

		// 맵 프리팹 Addressable 주소 (Assets/Project/Prefabs/Maps 하위는 파일명이 곧 주소)
		const string MapAddress = "Prefab_GridMap_01";

		static bool _tablesLoaded;

		void Awake()
		{
			//EnsureTablesLoaded();
		}

		void Start()
		{
			RunAsync().Forget();
		}

		// 맵을 먼저 로드/초기화한 뒤 유닛을 스폰 — UnitMover가 도는 시점엔 TilemapGrid가 준비됨
		async UniTaskVoid RunAsync()
		{
			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			await LoadMapAsync(ct);
			await SpawnAllAsync(ct);
		}

		// 맵 프리팹을 인스턴스화하고 Grid/Tilemap 참조를 모아 TilemapGrid.Setup 호출
		async UniTask LoadMapAsync(CancellationToken ct)
		{
			GameObject mapGo = await AddressableHelper.InstantiateAsync(MapAddress, null, true, ct);
			if (mapGo == null)
			{
				Debug.LogError($"[MapTestSpawner] 맵 인스턴스화 실패: {MapAddress}");
				return;
			}

			Grid grid = mapGo.GetComponent<Grid>();
			TilemapGrid tilemapGrid = mapGo.GetComponent<TilemapGrid>();
			if (grid == null || tilemapGrid == null)
			{
				Debug.LogError($"[MapTestSpawner] 맵 프리팹에 Grid/TilemapGrid 없음: {MapAddress}");
				return;
			}

			// 자식 Tilemap을 이름으로 ground/obstacle 구분
			Tilemap groundMap = null;
			Tilemap obstacleMap = null;
			Tilemap[] maps = mapGo.GetComponentsInChildren<Tilemap>(true);
			for (int i = 0; i < maps.Length; i++)
			{
				string n = maps[i].name;
				if (n.Contains("Obstacle") == true)
				{
					obstacleMap = maps[i];
				}
				else if (n.Contains("Floor") == true)
				{
					groundMap = maps[i];
				}
			}

			if (groundMap == null || obstacleMap == null)
			{
				Debug.LogError($"[MapTestSpawner] 맵 프리팹에서 Floor/Obstacle Tilemap을 찾지 못함: {MapAddress}");
				return;
			}

			tilemapGrid.Setup(grid, groundMap, obstacleMap);
		}

		async UniTask SpawnAllAsync(CancellationToken ct)
		{
			for (int i = 0; i < _heroes.Count; i++)
			{
				HeroEntry e = _heroes[i];
				Faction f = e.faction;
				if (f == Faction.None)
				{
					f = Faction.Player;
				}

				await UnitFactory.Instance.CreateHeroAsync(e.characterId, e.position, f, e.autoControl, ct);
			}

			for (int i = 0; i < _monsters.Count; i++)
			{
				MonsterEntry e = _monsters[i];
				Faction f = e.faction;
				if (f == Faction.None)
				{
					f = Faction.Enemy;
				}

				await UnitFactory.Instance.CreateMonsterAsync(e.monsterId, e.position, f, ct);
			}
		}

		// 테이블 로딩 — 부트스트랩 없는 테스트 씬에서 자기충족적으로 동작
		// 에디터: AssetDatabase 로 .bytes TextAsset 로드. 런타임 빌드는 별도 부트스트랩 가정.
		static void EnsureTablesLoaded()
		{
			if (_tablesLoaded == true)
			{
				return;
			}

			EDT.Loader loader = new EDT.Loader();
			bool ok = loader.LoadAll(OpenTableFile, null);
			if (ok == false)
			{
				Debug.LogError($"[MapTestSpawner] 테이블 로드 실패: {loader.CurrentFile} / {loader.Error}");
				return;
			}

			_tablesLoaded = true;
		}

		static BinaryReader OpenTableFile(string filename)
		{
#if UNITY_EDITOR
			string path = "Assets/Project/Data/Tables/" + filename;
			TextAsset ta = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path);
			if (ta == null)
			{
				Debug.LogError($"[MapTestSpawner] 테이블 파일 없음: {path}");
				return null;
			}

			return new BinaryReader(new MemoryStream(ta.bytes));
#else
			return null;
#endif
		}
	}
}
