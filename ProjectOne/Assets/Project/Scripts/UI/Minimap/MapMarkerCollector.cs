using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Field;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Quests;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 지금 맵의 지도 표시 목록을 맵 프리팹 데이터에서 모은다. 몬스터는 표시하지 않는다.
	//
	// - 필드보스: BossArea 가 붙은 스폰 포인트. 오늘 처치했으면 다음 06시까지 남은 시간을 표시한다.
	// - NPC: NpcSpawn 테이블이 지목한 NpcSpawnPoint. 퀘스트 등장 조건에 맞는 것만.
	// - 구역 관문: ZoneGate. 목적지 구역 이름.
	// - 그 밖의 오브젝트: MinimapMarker 를 붙인 것.
	public static class MapMarkerCollector
	{
		private const string BossIcon = "btn_icon_skull";
		private const string GateIcon = "btn_icon_arrow_go";
		private const string NpcIcon = "btn_icon_talk_1";

		private static readonly List<int> _mapIds = new List<int>();

		// 지금 맵의 지도 이미지. 맵이 없거나 아직 굽지 않은 맵이면 null.
		public static MinimapImage FindCurrentImage()
		{
			if (MapManager.HasInstance == false)
			{
				return null;
			}

			TilemapGrid grid = MapManager.Instance.Current;
			if (grid == null)
			{
				return null;
			}

			return grid.GetComponent<MinimapImage>();
		}

		public static void Collect(MinimapImage image, List<MapMarker> buffer)
		{
			buffer.Clear();
			if (image == null)
			{
				return;
			}

			TilemapGrid grid = image.GetComponent<TilemapGrid>();
			if (grid != null)
			{
				collectBosses(grid, buffer);
				collectGates(grid, buffer);
			}

			collectNpcs(buffer);
			collectCustom(image, buffer);
			RefreshBosses(buffer);
		}

		// 띄우지 않은 월드 맵 프리팹(에셋)의 지도 이미지 — 지도 화면에서 다른 월드를 볼 때. 없으면 null.
		public static MinimapImage FindImage(WorldMap world)
		{
			if (world == null || world.Grid == null)
			{
				return null;
			}

			return world.Grid.GetComponent<MinimapImage>();
		}

		// 띄우지 않은 월드 맵 프리팹(에셋)의 표시 목록. 월드는 원점에 놓이므로 에셋 좌표가 곧 월드 좌표다.
		public static void Collect(WorldMap world, List<MapMarker> buffer)
		{
			buffer.Clear();
			MinimapImage image = FindImage(world);
			if (image == null)
			{
				return;
			}

			collectBosses(world.Grid, buffer);
			collectGates(world.Grid, buffer);
			if (Account.Instance.Quests != null)
			{
				int cleared = Account.Instance.Quests.ClearedQuestId;
				IReadOnlyList<ZoneArea> zones = world.Zones;
				for (int i = 0; i < zones.Count; i++)
				{
					collectNpcsOfMap(zones[i].ZoneId, zones[i].NpcPoints, cleared, buffer);
				}
			}

			collectCustom(image, buffer);
			RefreshBosses(buffer);
		}

		// 필드보스의 생사와 남은 시간을 다시 읽는다 — 처치 후에는 이름 아래 줄에 다시 나올 때까지 남은 시간. 표시가 바뀐 표시가 하나라도 있으면 true.
		public static bool RefreshBosses(List<MapMarker> markers)
		{
			bool changed = false;
			string remain = null;
			for (int i = 0; i < markers.Count; i++)
			{
				MapMarker marker = markers[i];
				if (marker.Kind != MapMarkerKind.Boss)
				{
					continue;
				}

				bool dead = MonsterRespawnClock.Instance.IsBossKilledToday(marker.BossZoneId, marker.BossSpawnId);
				string label = marker.BossName;
				if (dead == true)
				{
					if (remain == null)
					{
						remain = DailyReset.FormatDuration((int)DailyReset.GetRemaining().TotalSeconds);
					}

					label = marker.BossName + "\n" + remain;
				}

				if (dead != marker.Dead || label != marker.Label)
				{
					marker.Dead = dead;
					marker.Label = label;
					changed = true;
				}
			}

			return changed;
		}

		// ── 수집 ──────────────────────────────────────────────────────

		private static void collectBosses(TilemapGrid grid, List<MapMarker> buffer)
		{
			IReadOnlyList<MonsterSpawnPoint> points = grid.SpawnPoints;
			for (int i = 0; i < points.Count; i++)
			{
				MonsterSpawnPoint point = points[i];
				if (point.GetComponent<BossArea>() == null)
				{
					continue;
				}

				Table_MonsterSpawn.Row row = findDailyRow(point.SpawnGroupId);
				if (row == null)
				{
					continue;
				}

				ZoneArea zone = point.GetComponentInParent<ZoneArea>(true);
				Table_Monster.Row monster = Table_Monster.Get(row.MonsterID);

				MapMarker marker = new MapMarker();
				marker.Kind = MapMarkerKind.Boss;
				marker.Position = point.Position;
				marker.Icon = BossIcon;
				marker.BossZoneId = (zone != null) ? zone.ZoneId : 0;
				marker.BossSpawnId = row.ID;
				marker.BossName = (monster != null) ? monster.Name : string.Empty;
				marker.Label = marker.BossName;
				buffer.Add(marker);
			}
		}

		private static Table_MonsterSpawn.Row findDailyRow(int groupId)
		{
			IReadOnlyList<Table_MonsterSpawn.Row> rows = MonsterCatalog.GetSpawnGroup(groupId);
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i].RespawnType == RespawnType.DailyReset)
				{
					return rows[i];
				}
			}

			return null;
		}

		private static void collectGates(TilemapGrid grid, List<MapMarker> buffer)
		{
			IReadOnlyList<ZoneGate> gates = grid.Gates;
			for (int i = 0; i < gates.Count; i++)
			{
				Table_Zone.Row zone = Table_Zone.Get(gates[i].TargetZoneId);
				MapMarker marker = new MapMarker();
				marker.Kind = MapMarkerKind.Gate;
				marker.Position = gates[i].transform.position;
				marker.Icon = GateIcon;
				marker.Label = (zone != null) ? zone.Name : string.Empty;
				buffer.Add(marker);
			}
		}

		private static void collectNpcs(List<MapMarker> buffer)
		{
			if (Account.Instance.Quests == null)
			{
				return;
			}

			int cleared = Account.Instance.Quests.ClearedQuestId;
			MapManager.Instance.CollectLoadedMapIds(_mapIds);
			for (int m = 0; m < _mapIds.Count; m++)
			{
				collectNpcsOfMap(_mapIds[m], MapManager.Instance.GetNpcSpawnPoints(_mapIds[m]), cleared, buffer);
			}
		}

		// 맵(월드면 구역) 하나의 NPC — 퀘스트 등장 조건에 맞는 것만.
		private static void collectNpcsOfMap(int mapId, IReadOnlyList<NpcSpawnPoint> points, int cleared, List<MapMarker> buffer)
		{
			IReadOnlyList<Table_NpcSpawn.Row> spawns = QuestCatalog.GetSpawnsOfMap(mapId);
			for (int i = 0; i < spawns.Count; i++)
			{
				Table_NpcSpawn.Row row = spawns[i];
				if (QuestCatalog.IsSpawnActive(row, cleared) == false)
				{
					continue;
				}

				NpcSpawnPoint point = findNpcPoint(points, row.SpawnPointID);
				Table_Npc.Row npc = Table_Npc.Get(row.NpcID);
				if (point == null || npc == null)
				{
					continue;
				}

				MapMarker marker = new MapMarker();
				marker.Kind = MapMarkerKind.Npc;
				marker.Position = point.Position;
				marker.Icon = npcIcon(npc.NpcType);
				marker.Label = string.IsNullOrEmpty(npc.Title) == false ? npc.Title : npc.Name;
				buffer.Add(marker);
			}
		}

		private static NpcSpawnPoint findNpcPoint(IReadOnlyList<NpcSpawnPoint> points, int spawnPointId)
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

		private static string npcIcon(NpcType type)
		{
			switch (type)
			{
				case NpcType.Shop:
					return "btn_icon_store_1";
				case NpcType.Blacksmith:
					return "btn_icon_anvil";
				case NpcType.Storage:
					return "btn_icon_chest_1";
				case NpcType.Portal:
					return "btn_icon_cyclone";
				default:
					return NpcIcon;
			}
		}

		private static void collectCustom(MinimapImage image, List<MapMarker> buffer)
		{
			MinimapMarker[] customs = image.GetComponentsInChildren<MinimapMarker>(false);
			for (int i = 0; i < customs.Length; i++)
			{
				MapMarker marker = new MapMarker();
				marker.Kind = MapMarkerKind.Custom;
				marker.Position = customs[i].Position;
				marker.Icon = customs[i].Icon;
				marker.Label = customs[i].Label;
				buffer.Add(marker);
			}
		}
	}
}
