using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Field;
using ProjectOne.Flow;
using UnityEngine;

namespace ProjectOne.Map
{
	// 목적지를 Table_Map.ID 하나로 받아 어디로 갈지 정하는 단일 진입점.
	//
	// 어느 상태로 갈지는 Map 테이블의 MapType 이 정한다 — 코드가 "이 버튼은 필드" 같은 분류표를
	// 따로 들면 테이블과 두 벌이 되어 같이 틀어진다. 개발용 워프 버튼과 월드 화면의 필드 이동이
	// 같은 판단을 하므로 여기 한 곳만 둔다.
	//
	// 네임스페이스를 정규화하는 이유 — using ProjectOne.Dungeon 을 넣으면 EDT.Dungeon enum 과
	// 충돌해 이 파일의 다른 Dungeon 참조가 깨진다.
	public static class MapNavigator
	{
		// 개발용 이동. 던전이라도 입장 횟수를 소모하지 않는다 — 반복 진입이 목적이다.
		// 정식 던전 진입은 EnterDungeon 을 쓴다.
		public static void MoveToMap(int mapId, CancellationToken ct)
		{
			Table_Map.Row map = Table_Map.Get(mapId);
			if (map == null)
			{
				Debug.LogWarning($"[MapNavigator] Map {mapId} 가 없습니다 — 이동 대상 ID 를 확인하세요.");
				return;
			}

			if (map.MapType == MapType.Town)
			{
				MoveToTown();
				return;
			}

			if (map.MapType == MapType.Field)
			{
				moveToField(mapId, ct);
				return;
			}

			if (map.MapType == MapType.Dungeon)
			{
				// 던전은 목적지가 (DungeonType, Stage) 지만 그 둘을 DungeonStage 테이블이 MapID 와 함께 들고 있다.
				// 버튼에 던전 전용 필드를 새로 다는 대신 맵으로 되찾는다 — 목적지 표현이 두 벌이 되지 않는다.
				Table_DungeonStage.Row stage = ProjectOne.Dungeon.DungeonProgress.FindStageRowByMapId(mapId);
				if (stage == null)
				{
					Debug.LogWarning($"[MapNavigator] Map {mapId} 를 쓰는 DungeonStage 가 없습니다.");
					return;
				}

				StartDungeon(stage.DungeonType, stage.Stage);
				return;
			}

			Debug.LogWarning($"[MapNavigator] 아직 지원하지 않는 이동 대상입니다 — Map {mapId} ({map.MapType})");
		}

		// 필드 밖(마을)이면 씬 전환, 필드 안이면 씬 전환 없이 이동/교체한다.
		// Map.ID 와 Field.ID 는 같은 값이다.
		private static void moveToField(int fieldId, CancellationToken ct)
		{
			if (FieldDirector.HasInstance == false)
			{
				GameFlow.Instance.ChangeStateAsync(new FieldState(fieldId)).Forget();
				return;
			}

			FieldDirector.Instance.ChangeActAsync(fieldId, ct).Forget();
		}

		// 마을 귀환. 마을은 Map.ID 없이 TownState 로 충분하다 — 마을 맵은 TownDirector 가 테이블에서 찾는다.
		public static void MoveToTown()
		{
			changeStateIfNeeded(new TownState(), typeof(TownState));
		}

		// 던전 씬으로 넘어간다. 입장 횟수 판정은 호출부가 이미 끝냈다고 본다.
		public static void StartDungeon(EDT.Dungeon type, int stage)
		{
			ProjectOne.Dungeon.DungeonContext ctx = new ProjectOne.Dungeon.DungeonContext(type, stage);
			GameFlow.Instance.ChangeStateAsync(new DungeonState(ctx)).Forget();
		}

		// 같은 상태로 다시 들어가면 씬을 새로 로드해 히어로가 재스폰되고 위치가 초기화된다.
		private static void changeStateIfNeeded(IGameState next, System.Type stateType)
		{
			if (GameFlow.Instance.CurrentState != null && GameFlow.Instance.CurrentState.GetType() == stateType)
			{
				return;
			}

			GameFlow.Instance.ChangeStateAsync(next).Forget();
		}
	}
}
