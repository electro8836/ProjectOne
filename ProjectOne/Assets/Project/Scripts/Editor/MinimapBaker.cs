using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using ProjectOne.Map;

namespace ProjectOne.EditorTools
{
	// 미니맵 지도 이미지 굽기 — 맵 프리팹마다 타일맵 셀을 단색으로 칠한 PNG 를 만들고
	// 프리팹의 MinimapImage 에 이미지와 범위를 채운다.
	//
	// 범위는 TilemapGrid 와 같은 기준(바닥 타일맵 cellBounds)이라 런타임 좌표와 픽셀이 그대로 맞는다.
	// 셀 판정도 TilemapGrid.IsCellWalkable 과 같다 — 벽(blockMap) / 물(obstacleMap) / 바닥(groundMap).
	// 건물 같은 오브젝트 장애물(MapBlocker)은 사각형으로 덧칠한다. 관문 장막은 열리고 닫히므로 칠하지 않는다.
	public static class MinimapBaker
	{
		private const string MapPrefabFolder = "Assets/Project/Prefabs/Maps";
		// 월드 도입 전의 구역별 필드 프리팹 — 월드 맵 프리팹이 대신하므로 굽지 않는다.
		private const string LegacyFieldFolder = "Assets/Project/Prefabs/Maps/Field/";
		private const string OutputFolder = "Assets/Project/Art/UI/Minimap";

		// 셀 하나를 몇 픽셀로 그릴지 — 확대해도 경계가 너무 뭉개지지 않을 만큼. 큰 맵은 최대 크기에 맞춰 줄인다.
		private const int PixelsPerCell = 4;
		private const int MaxTextureSize = 4096;

		private static readonly Color32 FloorColor = new Color32(150, 190, 232, 255);
		private static readonly Color32 WaterColor = new Color32(70, 112, 170, 255);
		private static readonly Color32 WallColor = new Color32(52, 66, 98, 255);
		private static readonly Color32 BlockerColor = new Color32(88, 108, 148, 255);
		private static readonly Color32 EmptyColor = new Color32(0, 0, 0, 0);

		[MenuItem("Tools/Minimap/지도 이미지 굽기 (전체 맵)")]
		private static void bakeAll()
		{
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new string[] { MapPrefabFolder });
			int baked = 0;
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (path.StartsWith(LegacyFieldFolder) == true)
				{
					continue;
				}

				EditorUtility.DisplayProgressBar("미니맵 굽기", path, (float)i / guids.Length);
				if (bake(path) == true)
				{
					baked++;
				}
			}

			EditorUtility.ClearProgressBar();
			AssetDatabase.SaveAssets();
			Debug.Log($"[MinimapBaker] 지도 이미지 {baked}개를 구웠습니다.");
		}

		private static bool bake(string prefabPath)
		{
			GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
			TilemapGrid grid = root.GetComponentInChildren<TilemapGrid>(true);
			if (grid == null)
			{
				PrefabUtility.UnloadPrefabContents(root);
				return false;
			}

			SerializedObject so = new SerializedObject(grid);
			Grid unityGrid = so.FindProperty("_grid").objectReferenceValue as Grid;
			Tilemap groundMap = so.FindProperty("_groundMap").objectReferenceValue as Tilemap;
			Tilemap obstacleMap = so.FindProperty("_obstacleMap").objectReferenceValue as Tilemap;
			Tilemap blockMap = so.FindProperty("_blockMap").objectReferenceValue as Tilemap;
			if (unityGrid == null || groundMap == null)
			{
				Debug.LogError($"[MinimapBaker] TilemapGrid 의 _grid / _groundMap 이 비었습니다: {prefabPath}");
				PrefabUtility.UnloadPrefabContents(root);
				return false;
			}

			// TilemapGrid.InitializeFlowField 와 같은 범위 계산
			BoundsInt bounds = groundMap.cellBounds;
			int width = bounds.size.x;
			int height = bounds.size.y;
			Vector2 cellSize = (Vector2)unityGrid.cellSize;
			Vector2 worldMin = (Vector2)unityGrid.GetCellCenterWorld(bounds.min) - cellSize * 0.5f;
			Vector2 worldMax = worldMin + new Vector2(cellSize.x * width, cellSize.y * height);

			int ppc = Mathf.Max(1, Mathf.Min(PixelsPerCell, MaxTextureSize / Mathf.Max(width, height)));
			int texWidth = width * ppc;
			int texHeight = height * ppc;
			Color32[] pixels = new Color32[texWidth * texHeight];

			bool[] reachable = findReachable(grid, unityGrid, groundMap, obstacleMap, blockMap, bounds);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Vector3Int cell = new Vector3Int(bounds.min.x + x, bounds.min.y + y, 0);
					Color32 color = EmptyColor;
					if (reachable != null && reachable[y * width + x] == false && isWalkable(groundMap, obstacleMap, blockMap, cell) == true)
					{
						// 성벽 바깥처럼 바닥 타일은 있지만 걸어서 닿지 못하는 곳은 비워 둔다.
						fillRect(pixels, texWidth, x * ppc, y * ppc, ppc, ppc, EmptyColor);
						continue;
					}

					if (blockMap != null && blockMap.GetTile(cell) != null)
					{
						color = WallColor;
					}
					else if (obstacleMap != null && obstacleMap.GetTile(cell) != null)
					{
						color = WaterColor;
					}
					else if (groundMap.GetTile(cell) != null)
					{
						color = FloorColor;
					}

					fillRect(pixels, texWidth, x * ppc, y * ppc, ppc, ppc, color);
				}
			}

			paintBlockers(grid, pixels, texWidth, texHeight, worldMin, cellSize, ppc);

			Texture2D texture = savePng(root.name, pixels, texWidth, texHeight);

			MinimapImage image = grid.GetComponent<MinimapImage>();
			if (image == null)
			{
				image = grid.gameObject.AddComponent<MinimapImage>();
			}

			Vector2 origin = (Vector2)grid.transform.position;
			image.SetBaked(texture, worldMin - origin, worldMax - origin);

			PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
			PrefabUtility.UnloadPrefabContents(root);
			return true;
		}

		private static bool isWalkable(Tilemap groundMap, Tilemap obstacleMap, Tilemap blockMap, Vector3Int cell)
		{
			if (groundMap.GetTile(cell) == null)
			{
				return false;
			}

			bool obstacle = (obstacleMap != null && obstacleMap.GetTile(cell) != null);
			bool block = (blockMap != null && blockMap.GetTile(cell) != null);
			return obstacle == false && block == false;
		}

		// 시작 지점(MapAnchor)들에서 걸어서 닿는 셀. 시작 지점이 없으면 null — 바닥을 전부 칠한다.
		private static bool[] findReachable(TilemapGrid grid, Grid unityGrid, Tilemap groundMap, Tilemap obstacleMap, Tilemap blockMap, BoundsInt bounds)
		{
			MapAnchor[] anchors = grid.GetComponentsInChildren<MapAnchor>(true);
			if (anchors.Length == 0)
			{
				return null;
			}

			int width = bounds.size.x;
			int height = bounds.size.y;
			bool[] visited = new bool[width * height];
			Queue<Vector2Int> queue = new Queue<Vector2Int>();
			for (int i = 0; i < anchors.Length; i++)
			{
				Vector3Int cell = unityGrid.WorldToCell(anchors[i].Position);
				enqueue(queue, visited, cell.x - bounds.min.x, cell.y - bounds.min.y, width, height);
			}

			while (queue.Count > 0)
			{
				Vector2Int p = queue.Dequeue();
				Vector3Int cell = new Vector3Int(bounds.min.x + p.x, bounds.min.y + p.y, 0);
				if (isWalkable(groundMap, obstacleMap, blockMap, cell) == false)
				{
					continue;
				}

				enqueue(queue, visited, p.x + 1, p.y, width, height);
				enqueue(queue, visited, p.x - 1, p.y, width, height);
				enqueue(queue, visited, p.x, p.y + 1, width, height);
				enqueue(queue, visited, p.x, p.y - 1, width, height);
			}

			return visited;
		}

		private static void enqueue(Queue<Vector2Int> queue, bool[] visited, int x, int y, int width, int height)
		{
			if (x < 0 || y < 0 || x >= width || y >= height || visited[y * width + x] == true)
			{
				return;
			}

			visited[y * width + x] = true;
			queue.Enqueue(new Vector2Int(x, y));
		}

		private static void paintBlockers(TilemapGrid grid, Color32[] pixels, int texWidth, int texHeight, Vector2 worldMin, Vector2 cellSize, int ppc)
		{
			MapBlocker[] blockers = grid.GetComponentsInChildren<MapBlocker>(true);
			for (int i = 0; i < blockers.Length; i++)
			{
				MapBlocker blocker = blockers[i];
				if (blocker.gameObject.activeInHierarchy == false || blocker.GetComponentInParent<ZoneGate>(true) != null)
				{
					continue;
				}

				Vector2 min;
				Vector2 max;
				if (blocker.GetWorldRect(out min, out max) == false)
				{
					continue;
				}

				int x0 = Mathf.Clamp(Mathf.RoundToInt((min.x - worldMin.x) / cellSize.x * ppc), 0, texWidth);
				int y0 = Mathf.Clamp(Mathf.RoundToInt((min.y - worldMin.y) / cellSize.y * ppc), 0, texHeight);
				int x1 = Mathf.Clamp(Mathf.RoundToInt((max.x - worldMin.x) / cellSize.x * ppc), 0, texWidth);
				int y1 = Mathf.Clamp(Mathf.RoundToInt((max.y - worldMin.y) / cellSize.y * ppc), 0, texHeight);
				fillRect(pixels, texWidth, x0, y0, x1 - x0, y1 - y0, BlockerColor);
			}
		}

		private static void fillRect(Color32[] pixels, int texWidth, int x0, int y0, int w, int h, Color32 color)
		{
			for (int y = y0; y < y0 + h; y++)
			{
				int row = y * texWidth;
				for (int x = x0; x < x0 + w; x++)
				{
					pixels[row + x] = color;
				}
			}
		}

		private static Texture2D savePng(string mapName, Color32[] pixels, int width, int height)
		{
			if (Directory.Exists(OutputFolder) == false)
			{
				Directory.CreateDirectory(OutputFolder);
			}

			Texture2D temp = new Texture2D(width, height, TextureFormat.RGBA32, false);
			temp.SetPixels32(pixels);
			temp.Apply();
			string assetPath = OutputFolder + "/Minimap_" + mapName + ".png";
			File.WriteAllBytes(assetPath, temp.EncodeToPNG());
			Object.DestroyImmediate(temp);

			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
			TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
			if (importer != null)
			{
				importer.textureType = TextureImporterType.Default;
				importer.alphaIsTransparency = true;
				importer.mipmapEnabled = true;
				importer.wrapMode = TextureWrapMode.Clamp;
				importer.filterMode = FilterMode.Bilinear;
				importer.npotScale = TextureImporterNPOTScale.None;
				importer.maxTextureSize = MaxTextureSize;
				importer.SaveAndReimport();
			}

			return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
		}
	}
}
